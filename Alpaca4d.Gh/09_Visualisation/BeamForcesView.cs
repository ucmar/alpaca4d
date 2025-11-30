using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Result;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    public class BeamForcesView : GH_Component
    {
        private Alpaca4d.Model _model = null;
        private List<Mesh> _forceDiagramMeshes = new List<Mesh>();
        private int _forceType = 0;
        private int _step = 0;
        private double _scale = 1.0;
        
        // Color properties for positive and negative values
        public System.Drawing.Color PositiveColor { get; set; } = System.Drawing.Color.FromArgb(255, 139, 0, 0); // Dark red (wine)
        public System.Drawing.Color NegativeColor { get; set; } = System.Drawing.Color.FromArgb(255, 0, 0, 139); // Dark blue
        
        private System.Drawing.Color _positiveColorInput = System.Drawing.Color.FromArgb(255, 139, 0, 0);
        private System.Drawing.Color _negativeColorInput = System.Drawing.Color.FromArgb(255, 0, 0, 139);

        public BeamForcesView()
          : base("Beam Forces View (Alpaca4d)", "Beam Forces View",
            "Visualize Beam Force Diagrams in the viewport",
            "Alpaca4d", "09_Visualisation")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The Alpaca Model", GH_ParamAccess.item);
            pManager.AddIntegerParameter("ForceType", "ForceType", "Force type to display: 0=N, 1=Vy, 2=Vz, 3=Torsion, 4=My, 5=Mz", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Analysis step", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Scale", "Scale", "Diagram scale factor", GH_ParamAccess.item, 1.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("PositiveColor", "PositiveColor", "Color for positive force values", GH_ParamAccess.item, System.Drawing.Color.FromArgb(255, 139, 0, 0));
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("NegativeColor", "NegativeColor", "Color for negative force values", GH_ParamAccess.item, System.Drawing.Color.FromArgb(255, 0, 0, 139));
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Info", "Info", "Information about force diagrams");
        }

        /// <summary>
        /// This is called before SolveInstance to update value lists.
        /// </summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            
            // Clear cached meshes
            _forceDiagramMeshes.Clear();
            
            // Update value list for ForceType input
            var forceTypeNames = new List<string> 
            { 
                "N", "Vy", "Vz", "Torsion", "My", "Mz" 
            };
            var forceTypeValues = new List<int> { 0, 1, 2, 3, 4, 5 };
            
            ValueList.UpdateValueLists(this, 1, forceTypeNames, forceTypeValues, GH_ValueListMode.DropDown, 0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _forceType = 0;
            _step = 0;
            _scale = 1.0;

            if (!DA.GetData(0, ref _model)) return;
            DA.GetData(1, ref _forceType);
            DA.GetData(2, ref _step);
            DA.GetData(3, ref _scale);
            DA.GetData(4, ref _positiveColorInput);
            DA.GetData(5, ref _negativeColorInput);
            
            // Update public properties
            PositiveColor = _positiveColorInput;
            NegativeColor = _negativeColorInput;

            // Validate force type
            if (_forceType < 0 || _forceType > 5)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ForceType must be between 0 and 5");
                return;
            }

            // Read force data
            (var n, var mz, var vy, var my, var vz, var t) = Alpaca4d.Result.Read.ForceBeamColumn(_model, _step);

            // Select the appropriate force component based on _forceType
            var forceData = GetForceComponent(_forceType, n, vy, vz, t, my, mz);

            // Create force diagrams for each beam
            _forceDiagramMeshes.Clear();
            for (int i = 0; i < _model.Beams.Count; i++)
            {
                if (i < forceData.Count)
                {
                    var beam = _model.Beams[i];
                    var forces = forceData[i];
                    
                    // Create diagram mesh for this beam
                    var diagramMesh = CreateBeamForceDiagram((Alpaca4d.Element.ForceBeamColumn)beam, forces, _forceType, _scale);
                    if (diagramMesh != null)
                    {
                        _forceDiagramMeshes.Add(diagramMesh);
                    }
                }
            }

            // Output info
            string[] forceNames = { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
            string info = $"Force: {forceNames[_forceType]}, Beams: {_model.Beams.Count}, Step: {_step}, Scale: {_scale:F2}";
            DA.SetData(0, info);

            // Ensure viewport updates
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>
        /// Helper method to select the appropriate force component
        /// </summary>
        private List<List<double>> GetForceComponent(int forceType,
            List<List<double>> n, List<List<double>> vy, List<List<double>> vz,
            List<List<double>> t, List<List<double>> my, List<List<double>> mz)
        {
            switch (forceType)
            {
                case 0: return n;    // Normal force
                case 1: return vy;   // Shear Y
                case 2: return vz;   // Shear Z
                case 3: return t;    // Torsion (Mx)
                case 4: return my;   // Moment Y
                case 5: return mz;   // Moment Z
                default: return new List<List<double>>();
            }
        }

        /// <summary>
        /// Creates a force diagram mesh for a single beam
        /// </summary>
        private Mesh CreateBeamForceDiagram(Alpaca4d.Element.ForceBeamColumn beam, List<double> forces, int forceType, double scale)
        {
            if (forces == null || forces.Count == 0) return null;

            var curve = beam.Curve;
            var integrationPoints = forces.Count;

            // Get reference direction based on force type
            Vector3d referenceDirection = GetReferenceDirection(beam, forceType);

            // Create points along the beam at integration point locations
            var beamPoints = new List<Point3d>();
            var diagramPoints = new List<Point3d>();
            var colors = new List<System.Drawing.Color>();

            for (int i = 0; i < integrationPoints; i++)
            {
                // Get parameter along curve (assuming uniform distribution)
                double t = (double)i / (integrationPoints - 1);
                Point3d pointOnBeam = curve.PointAtNormalizedLength(t);
                
                // Offset point perpendicular to beam in reference direction
                Vector3d offset = referenceDirection * forces[i] * scale;
                Point3d diagramPoint = pointOnBeam + offset;
                
                beamPoints.Add(pointOnBeam);
                diagramPoints.Add(diagramPoint);
                
                // Determine color based on force value (positive or negative)
                System.Drawing.Color color = forces[i] >= 0 ? PositiveColor : NegativeColor;
                colors.Add(color);
            }

            // Create closed mesh from points with colors
            Mesh mesh = CreateClosedDiagramMesh(beamPoints, diagramPoints, colors);
            
            return mesh;
        }

        /// <summary>
        /// Creates a closed mesh from beam points and diagram points with vertex colors
        /// Creates a closed polygon: beam points forward, then diagram points backward
        /// </summary>
        private Mesh CreateClosedDiagramMesh(List<Point3d> beamPoints, List<Point3d> diagramPoints, List<System.Drawing.Color> colors)
        {
            if (beamPoints.Count < 2 || diagramPoints.Count < 2) return null;

            int n = beamPoints.Count;

            // Create a closed polyline: beam points -> last diagram point -> diagram points reversed -> first beam point
            var boundaryPoints = new List<Point3d>();
            var boundaryColors = new List<System.Drawing.Color>();
            
            // Add beam points forward (0 to n-1) with corresponding colors
            for (int i = 0; i < n; i++)
            {
                boundaryPoints.Add(beamPoints[i]);
                boundaryColors.Add(colors[i]);
            }
            
            // Add diagram points backward (n-1 to 0) with corresponding colors
            for (int i = n - 1; i >= 0; i--)
            {
                boundaryPoints.Add(diagramPoints[i]);
                boundaryColors.Add(colors[i]);
            }
            
            // Create closed polyline
            var polyline = new Polyline(boundaryPoints);
            polyline.Add(boundaryPoints[0]); // Close the polyline
            
            // Create mesh from closed polyline using Delaunay or simple triangulation
            var mesh = new Mesh();
            
            // Add all vertices with colors
            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                mesh.Vertices.Add(boundaryPoints[i]);
                mesh.VertexColors.Add(boundaryColors[i]);
            }
            
            // Create faces using ear clipping triangulation approach
            // For a convex polygon (which a force diagram typically is), simple fan works
            // For better results, create triangles from opposite edges
            
            // Method: Create triangle strip connecting beam edge to diagram edge
            for (int i = 0; i < n - 1; i++)
            {
                // Vertices on beam edge: i, i+1
                // Corresponding vertices on diagram edge (reversed): 2n-1-i, 2n-2-i
                int b0 = i;
                int b1 = i + 1;
                int d0 = 2 * n - 1 - i;
                int d1 = 2 * n - 2 - i;
                
                // Create two triangles for each quad
                mesh.Faces.AddFace(b0, b1, d1);
                mesh.Faces.AddFace(b0, d1, d0);
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();

            return mesh;
        }

        /// <summary>
        /// Gets the reference direction for plotting based on force type
        /// </summary>
        private Vector3d GetReferenceDirection(Alpaca4d.Element.ForceBeamColumn beam, int forceType)
        {
            var curve = beam.Curve;
            var localZ = beam.GeomTransf.LocalZ;
            var localY = beam.GeomTransf.LocalY;

            // LocalX is the beam direction
            Vector3d localX = curve.PointAtEnd - curve.PointAtStart;
            localX.Unitize();

            switch (forceType)
            {
                case 0: // Normal force (N)
                    // Plot on plane from cross product of LocalX and GlobalZ
                    Vector3d globalZ = new Vector3d(0, 0, 1);
                    Vector3d nDirection = Vector3d.CrossProduct(localX, globalZ);
                    nDirection.Unitize();
                    if (nDirection.Length < 0.01) // If beam is vertical
                    {
                        nDirection = Vector3d.CrossProduct(localX, new Vector3d(1, 0, 0));
                        nDirection.Unitize();
                    }
                    return nDirection;

                case 1: // Shear Y (Vy)
                    // Plot along local Y
                    return localY;

                case 2: // Shear Z (Vz)
                    // Plot along local Z
                    return localZ;

                case 3: // Torsion (Mx)
                    // Plot on plane from cross product of LocalX and GlobalZ
                    Vector3d globalZ2 = new Vector3d(0, 0, 1);
                    Vector3d tDirection = Vector3d.CrossProduct(localX, globalZ2);
                    tDirection.Unitize();
                    if (tDirection.Length < 0.01) // If beam is vertical
                    {
                        tDirection = Vector3d.CrossProduct(localX, new Vector3d(1, 0, 0));
                        tDirection.Unitize();
                    }
                    return tDirection;

                case 4: // Moment Y (My)
                    // Plot along local Y
                    return localY;

                case 5: // Moment Z (Mz)
                    // Plot along local Z
                    return localZ;

                default:
                    return Vector3d.ZAxis;
            }
        }

        /// <summary>
        /// This method draws the force diagrams in the viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            // Draw beam reference lines
            foreach (var beam in _model.Beams)
            {
                args.Display.DrawCurve(beam.Curve, System.Drawing.Color.Gray, 1);
            }

            // Draw force diagram meshes with vertex colors
            foreach (var mesh in _forceDiagramMeshes)
            {
                args.Display.DrawMeshFalseColors(mesh);
                args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 1);
            }
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox
        {
            get
            {
                return new BoundingBox(
                    new Point3d(-1e9, -1e9, -1e9),
                    new Point3d(1e9, 1e9, 1e9)
                );
            }
        }

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Beam_Forces__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{3C8E5F2D-7A4B-4E9F-8B1C-6D9E7F8A3C4D}");
    }
}

