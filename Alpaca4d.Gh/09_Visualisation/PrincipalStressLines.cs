using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Alpaca4d.Result;
using Alpaca4d.Gh.LilyPad;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Creates principal stress lines (direction 1 and 2) on shell elements of an Alpaca model.
    /// Combines deformation extraction from the Alpaca model with LilyPad-style principal field
    /// and streamline generation.
    /// </summary>
    public class PrincipalStressLines : GH_Component
    {
        public PrincipalStressLines()
          : base("Principal Stress Lines (Alpaca4d)", "Principal Stress Lines",
            "Plot principal stress lines on shell elements using nodal displacements/rotations",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "Alpaca4d model with analysed shell elements", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step", "Step", "Analysis step index", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddPointParameter("Seed", "S", "Seed point for stress lines", GH_ParamAccess.item);
            pManager.AddNumberParameter("Step Tolerance", "T", "Integration step size along stress lines", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Max. Error", "Err", "Adaptive step error tolerance (0 disables adaptation)", GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter("Poisson's ratio", "v", "Poisson's ratio used for principal direction evaluation", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Bending Stress Lines", "BSL", "If true use rotations (bending), if false use in-plane displacements", GH_ParamAccess.item, false);

            pManager.AddNumberParameter("Separation", "dSep", "Separation distance for neighbouring stress lines", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Test Dist.", "dTest", "Distance to existing lines below which new streamlines stop (0 disables check)", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Stress Lines Dir1", "SL1", "Principal stress lines for direction 1", GH_ParamAccess.list);
            pManager.AddCurveParameter("Stress Lines Dir2", "SL2", "Principal stress lines for direction 2", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var model = new Alpaca4d.Model();
            if (!DA.GetData(0, ref model)) return;

            int step = 0;
            DA.GetData(1, ref step);

            Point3d seed = new Point3d();
            DA.GetData(2, ref seed);

            double stepTolerance = 0.5;
            DA.GetData(3, ref stepTolerance);

            double maxError = 0.0;
            DA.GetData(4, ref maxError);

            double poisson = 0.0;
            DA.GetData(5, ref poisson);

            bool bending = false;
            DA.GetData(6, ref bending);

            double dSep = 1.0;
            DA.GetData(7, ref dSep);

            double dTest = 0.0;
            DA.GetData(8, ref dTest);

            if (model.Shells == null || model.Shells.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Model has no shell elements. Nothing to draw.");
                return;
            }

            // ---------------------------------------------------------------------
            // 1. Gather nodal translations and rotations from Alpaca results
            // ---------------------------------------------------------------------
            var dispDict = model.NodalDisplacements(step);

            // Build rotations dictionary (similar to NodalDisplacements but for ROTATION)
            var rotDict = new Dictionary<int?, Vector3d>();
            var rotations = Alpaca4d.Result.Read.NodalOutput(model, step, ResultType.ROTATION).ToList();
            foreach (var node in model.Nodes)
            {
                if (node.Id.HasValue)
                {
                    int index = node.Id.Value - 1;
                    if (index >= 0 && index < rotations.Count)
                        rotDict[node.Id] = rotations[index];
                }
            }

            // ---------------------------------------------------------------------
            // 2. Build combined mesh + principal direction elements (sigma1/sigma2)
            // ---------------------------------------------------------------------
            var combinedMesh = new Mesh();
            var sigma1Elements = new List<PrincipalElement>();
            var sigma2Elements = new List<PrincipalElement>();

            foreach (var shell in model.Shells)
            {
                var shellMesh = shell.Mesh;
                if (shellMesh == null || shellMesh.Faces.Count == 0)
                    continue;

                if (shell.IndexNodes == null || shell.IndexNodes.Count != shellMesh.Vertices.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Shell element has inconsistent IndexNodes and mesh vertices.");
                    return;
                }

                // Append shell mesh to combined domain mesh
                combinedMesh.Append(shellMesh);

                // For each face, create corresponding principal element for dir1/dir2
                for (int i = 0; i < shellMesh.Faces.Count; i++)
                {
                    var face = shellMesh.Faces[i];

                    if (face.IsQuad)
                    {
                        int p1 = face.A;
                        int p2 = face.B;
                        int p3 = face.D;
                        int p4 = face.C;

                        Point3d point1 = shellMesh.Vertices[p1];
                        Point3d point2 = shellMesh.Vertices[p2];
                        Point3d point3 = shellMesh.Vertices[p3];
                        Point3d point4 = shellMesh.Vertices[p4];

                        // Optional warp check similar to LilyPad
                        Plane fitPlane;
                        double maxDeviation;
                        if (Plane.FitPlaneToPoints(
                                new List<Point3d> { point1, point2, point3, point4 },
                                out fitPlane, out maxDeviation) != 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Quad face {i} could not fit a plane.");
                            continue;
                        }

                        if (maxDeviation > 0.01)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                $"Quad face {i} is too warped. Plane deviation: {maxDeviation}");
                            continue;
                        }

                        int? n1 = shell.IndexNodes[p1];
                        int? n2 = shell.IndexNodes[p2];
                        int? n3 = shell.IndexNodes[p3];
                        int? n4 = shell.IndexNodes[p4];

                        if (!n1.HasValue || !n2.HasValue || !n3.HasValue || !n4.HasValue)
                            continue;

                        Vector3d u1 = bending ? rotDict[n1] : dispDict[n1];
                        Vector3d u2 = bending ? rotDict[n2] : dispDict[n2];
                        Vector3d u3 = bending ? rotDict[n3] : dispDict[n3];
                        Vector3d u4 = bending ? rotDict[n4] : dispDict[n4];

                        var quadSigma1 = new Quad4Element(point1, point2, point3, point4, u1, u2, u3, u4, poisson, !bending);
                        quadSigma1.ChangeDirection(1);
                        sigma1Elements.Add(new PrincipalElement(quadSigma1));

                        var quadSigma2 = new Quad4Element(point1, point2, point3, point4, u1, u2, u3, u4, poisson, !bending);
                        quadSigma2.ChangeDirection(2);
                        sigma2Elements.Add(new PrincipalElement(quadSigma2));
                    }
                    else if (face.IsTriangle)
                    {
                        int p1 = face.A;
                        int p2 = face.B;
                        int p3 = face.C;

                        Point3d point1 = shellMesh.Vertices[p1];
                        Point3d point2 = shellMesh.Vertices[p2];
                        Point3d point3 = shellMesh.Vertices[p3];

                        int? n1 = shell.IndexNodes[p1];
                        int? n2 = shell.IndexNodes[p2];
                        int? n3 = shell.IndexNodes[p3];

                        if (!n1.HasValue || !n2.HasValue || !n3.HasValue)
                            continue;

                        Vector3d u1 = bending ? rotDict[n1] : dispDict[n1];
                        Vector3d u2 = bending ? rotDict[n2] : dispDict[n2];
                        Vector3d u3 = bending ? rotDict[n3] : dispDict[n3];

                        var triSigma1 = new Tri3Element(point1, point2, point3, u1, u2, u3, poisson, !bending);
                        triSigma1.ChangeDirection(1);
                        sigma1Elements.Add(new PrincipalElement(triSigma1));

                        var triSigma2 = new Tri3Element(point1, point2, point3, u1, u2, u3, poisson, !bending);
                        triSigma2.ChangeDirection(2);
                        sigma2Elements.Add(new PrincipalElement(triSigma2));
                    }
                }
            }

            if (combinedMesh.Faces.Count == 0 || sigma1Elements.Count == 0 || sigma2Elements.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid shell faces found for principal stress line generation.");
                return;
            }

            if (sigma1Elements.Count != combinedMesh.Faces.Count ||
                sigma2Elements.Count != combinedMesh.Faces.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Internal error: element count does not match mesh face count.");
                return;
            }

            // ---------------------------------------------------------------------
            // 3. Build principal meshes for direction 1 and 2
            // ---------------------------------------------------------------------
            var field1 = new FieldMesh(sigma1Elements, combinedMesh);
            var field2 = new FieldMesh(sigma2Elements, combinedMesh);

            var principal1 = new PrincipalMesh(field1);
            var principal2 = new PrincipalMesh(field2);

            // ---------------------------------------------------------------------
            // 4. Generate stress lines using neighbour seeding
            // ---------------------------------------------------------------------
            const int rk4Method = 4;

            var streamlines1 = new Streamlines(principal1, stepTolerance, rk4Method, maxError, dTest);
            var streamlines2 = new Streamlines(principal2, stepTolerance, rk4Method, maxError, dTest);

            List<Polyline> lines1;
            List<Polyline> lines2;

            try
            {
                lines1 = streamlines1.CreateStreamlines(seed, 1, dSep);
                lines2 = streamlines2.CreateStreamlines(seed, 1, dSep);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error generating stress lines: {ex.Message}");
                return;
            }

            DA.SetDataList(0, lines1);
            DA.SetDataList(1, lines2);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.shellStress;

        public override Guid ComponentGuid => new Guid("{E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2}");
    }
}


