using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Gh.LilyPad
{
    /// <summary>
    /// Quad 4-node element (bilinear) used to evaluate principal stress directions.
    /// Ported and lightly trimmed from LilyPad.
    /// </summary>
    class Quad4Element
    {
        // Nodal displacements/rotations
        public Vector3d U1;
        public Vector3d U2;
        public Vector3d U3;
        public Vector3d U4;

        // Nodal positions in element plane coordinates
        public Point3d P1;
        public Point3d P2;
        public Point3d P3;
        public Point3d P4;

        public double v;
        public Plane ElementPlane;
        public bool Inplane;

        public int Direction;

        private double _xi;
        private double _eta;

        public Point3d NaturalCoordinate
        {
            get { return new Point3d(_xi, _eta, 0.0); }
            set
            {
                _xi = value.X;
                _eta = value.Y;
            }
        }

        // Shape functions
        private double N1;
        private double N2;
        private double N3;
        private double N4;

        private double N1xi;
        private double N1eta;
        private double N2xi;
        private double N2eta;
        private double N3xi;
        private double N3eta;
        private double N4xi;
        private double N4eta;

        private double N1x;
        private double N1y;
        private double N2x;
        private double N2y;
        private double N3x;
        private double N3y;
        private double N4x;
        private double N4y;

        // Strains
        private double EpsilonX;
        private double EpsilonY;
        private double GammaXY;

        // Stresses
        private double SigmaX;
        private double SigmaY;
        private double TauXY;

        private double Theta;

        // Jacobian
        private double Dxdxi;
        private double Dxdeta;
        private double Dydxi;
        private double Dydeta;
        private double DetJ;

        public Quad4Element()
        {
            P1 = new Point3d();
            P2 = new Point3d();
            P3 = new Point3d();
            P4 = new Point3d();
            U1 = new Vector3d();
            U2 = new Vector3d();
            U3 = new Vector3d();
            U4 = new Vector3d();
            v = 0.0;
            Direction = 0;
            Inplane = true;
        }

        public Quad4Element(Point3d p1, Point3d p2, Point3d p3, Point3d p4,
                            Vector3d u1, Vector3d u2, Vector3d u3, Vector3d u4,
                            double poissons, bool inplane)
        {
            Inplane = inplane;

            // Element plane from geometry
            ElementPlane = new Plane(p1, p2 - p1, p3 - p1);

            // Remap the points to element plane
            ElementPlane.RemapToPlaneSpace(p1, out P1);
            ElementPlane.RemapToPlaneSpace(p2, out P2);
            ElementPlane.RemapToPlaneSpace(p3, out P3);
            ElementPlane.RemapToPlaneSpace(p4, out P4);

            // Project displacements into element plane coordinates
            if (Inplane)
            {
                U1 = OrientVector(u1, ElementPlane, Plane.WorldXY);
                U2 = OrientVector(u2, ElementPlane, Plane.WorldXY);
                U3 = OrientVector(u3, ElementPlane, Plane.WorldXY);
                U4 = OrientVector(u4, ElementPlane, Plane.WorldXY);
            }
            else
            {
                U1 = MathForm(OrientVector(u1, ElementPlane, Plane.WorldXY));
                U2 = MathForm(OrientVector(u2, ElementPlane, Plane.WorldXY));
                U3 = MathForm(OrientVector(u3, ElementPlane, Plane.WorldXY));
                U4 = MathForm(OrientVector(u4, ElementPlane, Plane.WorldXY));
            }

            v = poissons;
            Direction = 0;
        }

        public void ChangeDirection(int dir)
        {
            Direction = dir;
        }

        public Point3d CalculateNaturalCoordinate(Point3d cartesianPoint)
        {
            // Initial corners in Cartesian and natural space
            Point3d carte1 = P1;
            Point3d carte2 = P2;
            Point3d carte3 = P3;
            Point3d carte4 = P4;

            Point3d natur1 = new Point3d(-1.0, 1.0, 0.0);
            Point3d natur2 = new Point3d(1.0, 1.0, 0.0);
            Point3d natur3 = new Point3d(-1.0, -1.0, 0.0);
            Point3d natur4 = new Point3d(1.0, -1.0, 0.0);

            Point3d carte5 = new Point3d();
            Point3d carte6 = new Point3d();
            Point3d natur5 = new Point3d();
            Point3d natur6 = new Point3d();

            PolylineCurve partA;

            int side = 0;

            // Binary search in natural space
            for (int i = 0; i < 16; i++)
            {
                if (side == 0)
                {
                    // Left/right split
                    natur5 = new Point3d((natur1.X + natur2.X) / 2, (natur1.Y + natur2.Y) / 2, 0.0);
                    natur6 = new Point3d((natur3.X + natur4.X) / 2, (natur3.Y + natur4.Y) / 2, 0.0);
                    carte5 = CartesianCoordinates(natur5);
                    carte6 = CartesianCoordinates(natur6);

                    partA = new PolylineCurve(new[] { carte1, carte5, carte6, carte3, carte1 });

                    int testContainment = (int)partA.Contains(
                        cartesianPoint,
                        new Plane(Point3d.Origin, Vector3d.ZAxis),
                        0.00001);

                    if (testContainment == 1 || testContainment == 3)
                    {
                        natur2 = natur5;
                        natur4 = natur6;
                        carte2 = carte5;
                        carte4 = carte6;
                    }
                    else
                    {
                        natur1 = natur5;
                        natur3 = natur6;
                        carte1 = carte5;
                        carte3 = carte6;
                    }

                    side = 1;
                }
                else
                {
                    // Top/bottom split
                    natur5 = new Point3d((natur1.X + natur3.X) / 2, (natur1.Y + natur3.Y) / 2, 0.0);
                    natur6 = new Point3d((natur2.X + natur4.X) / 2, (natur2.Y + natur4.Y) / 2, 0.0);
                    carte5 = CartesianCoordinates(natur5);
                    carte6 = CartesianCoordinates(natur6);

                    partA = new PolylineCurve(new[] { carte1, carte2, carte6, carte5, carte1 });

                    int testContainment = (int)partA.Contains(
                        cartesianPoint,
                        new Plane(Point3d.Origin, Vector3d.ZAxis),
                        0.00001);

                    if (testContainment == 1 || testContainment == 3)
                    {
                        natur3 = natur5;
                        natur4 = natur6;
                        carte3 = carte5;
                        carte4 = carte6;
                    }
                    else
                    {
                        natur1 = natur5;
                        natur2 = natur6;
                        carte1 = carte5;
                        carte2 = carte6;
                    }

                    side = 0;
                }
            }

            return (natur1 + natur2 + natur3 + natur4) / 4;
        }

        public Point3d CartesianCoordinates(Point3d naturalPoint)
        {
            NaturalCoordinate = naturalPoint;
            CalculateShapeFunctionValues();

            double x = N1 * P1.X + N2 * P2.X + N3 * P3.X + N4 * P4.X;
            double y = N1 * P1.Y + N2 * P2.Y + N3 * P3.Y + N4 * P4.Y;
            return new Point3d(x, y, 0.0);
        }

        private void CalculateJacobianTerms()
        {
            Dxdxi = N1xi * P1.X + N2xi * P2.X + N3xi * P3.X + N4xi * P4.X;
            Dxdeta = N1eta * P1.X + N2eta * P2.X + N3eta * P3.X + N4eta * P4.X;
            Dydxi = N1xi * P1.Y + N2xi * P2.Y + N3xi * P3.Y + N4xi * P4.Y;
            Dydeta = N1eta * P1.Y + N2eta * P2.Y + N3eta * P3.Y + N4eta * P4.Y;
        }

        private void CalculateJacobianDeterminant()
        {
            DetJ = Dydeta * Dxdxi - Dydxi * Dxdeta;
        }

        private void CalculateShapeFunctionValues()
        {
            N1 = 0.25 * (1 - _xi) * (1 + _eta);
            N2 = 0.25 * (1 + _xi) * (1 + _eta);
            N3 = 0.25 * (1 - _xi) * (1 - _eta);
            N4 = 0.25 * (1 + _xi) * (1 - _eta);
        }

        private void CalculateDifferentiatedNaturalShapeFunctionValues()
        {
            N1xi = -0.25 * _eta - 0.25;
            N1eta = -0.25 * _xi + 0.25;
            N2xi = 0.25 * _eta + 0.25;
            N2eta = 0.25 * _xi + 0.25;
            N3xi = 0.25 * _eta - 0.25;
            N3eta = 0.25 * _xi - 0.25;
            N4xi = -0.25 * _eta + 0.25;
            N4eta = -0.25 * _xi - 0.25;
        }

        private void CalculateDifferentiatedCartesianShapeFunctionValues()
        {
            CalculateDifferentiatedNaturalShapeFunctionValues();
            CalculateJacobianTerms();
            CalculateJacobianDeterminant();

            N1x = 1 / DetJ * (Dydeta * N1xi - Dydxi * N1eta);
            N1y = 1 / DetJ * (-Dxdeta * N1xi + Dxdxi * N1eta);

            N2x = 1 / DetJ * (Dydeta * N2xi - Dydxi * N2eta);
            N2y = 1 / DetJ * (-Dxdeta * N2xi + Dxdxi * N2eta);

            N3x = 1 / DetJ * (Dydeta * N3xi - Dydxi * N3eta);
            N3y = 1 / DetJ * (-Dxdeta * N3xi + Dxdxi * N3eta);

            N4x = 1 / DetJ * (Dydeta * N4xi - Dydxi * N4eta);
            N4y = 1 / DetJ * (-Dxdeta * N4xi + Dxdxi * N4eta);
        }

        public Vector3d Evaluate(Point3d loc)
        {
            Point3d location;
            ElementPlane.RemapToPlaneSpace(loc, out location);

            NaturalCoordinate = CalculateNaturalCoordinate(location);

            CalculateDifferentiatedCartesianShapeFunctionValues();
            CalculateStrains();
            CalculateStresses();
            CalculateTheta();

            if (Direction == 1)
                return OrientVector(new Vector3d(Math.Cos(Theta), Math.Sin(Theta), 0), Plane.WorldXY, ElementPlane);
            if (Direction == 2)
                return OrientVector(new Vector3d(-Math.Sin(Theta), Math.Cos(Theta), 0), Plane.WorldXY, ElementPlane);

            return Vector3d.Zero;
        }

        private void CalculateStrains()
        {
            EpsilonX = N1x * U1.X + N2x * U2.X + N3x * U3.X + N4x * U4.X;
            EpsilonY = N1y * U1.Y + N2y * U2.Y + N3y * U3.Y + N4y * U4.Y;
            GammaXY =
                N1y * U1.X + N2y * U2.X + N3y * U3.X + N4y * U4.X +
                N1x * U1.Y + N2x * U2.Y + N3x * U3.Y + N4x * U4.Y;
        }

        private void CalculateStresses()
        {
            SigmaX = EpsilonX + v * EpsilonY;
            SigmaY = EpsilonY + v * EpsilonX;
            TauXY = (1 - v) / 2 * GammaXY;
        }

        private void CalculateTheta()
        {
            if (SigmaX == SigmaY)
            {
                Theta = 0.0;
            }
            else
            {
                Theta = Math.Atan(2 * TauXY / (SigmaX - SigmaY)) / 2;

                if (SigmaY > (SigmaX + SigmaY) / 2)
                {
                    if (TauXY > 0) Theta -= Math.PI / 2;
                    else Theta += Math.PI / 2;
                }
            }
        }

        private Vector3d OrientVector(Vector3d vector, Plane plane0, Plane plane1)
        {
            Transform orient = Transform.PlaneToPlane(plane0, plane1);
            vector.Transform(orient);
            return vector;
        }

        private Vector3d MathForm(Vector3d vector)
        {
            return new Vector3d(vector.Y, -vector.X, 0.0);
        }
    }

    /// <summary>
    /// Triangular 3-node element used to evaluate principal stress directions.
    /// Ported and lightly trimmed from LilyPad.
    /// </summary>
    class Tri3Element
    {
        public Vector3d U1;
        public Vector3d U2;
        public Vector3d U3;
        public Point3d P1;
        public Point3d P2;
        public Point3d P3;
        public double v;
        public Plane ElementPlane;
        public bool Inplane;

        public int Direction;

        private double _xi;
        private double _eta;

        public Point3d NaturalCoordinate
        {
            get { return new Point3d(_xi, _eta, 0.0); }
            set
            {
                _xi = value.X;
                _eta = value.Y;
            }
        }

        private double N1;
        private double N2;
        private double N3;

        private double N1xi;
        private double N1eta;
        private double N2xi;
        private double N2eta;
        private double N3xi;
        private double N3eta;

        private double N1x;
        private double N1y;
        private double N2x;
        private double N2y;
        private double N3x;
        private double N3y;

        private double EpsilonX;
        private double EpsilonY;
        private double GammaXY;

        private double SigmaX;
        private double SigmaY;
        private double TauXY;

        private double Theta;

        private double Dxdxi;
        private double Dxdeta;
        private double Dydxi;
        private double Dydeta;
        private double DetJ;

        public Tri3Element()
        {
            P1 = new Point3d();
            P2 = new Point3d();
            P3 = new Point3d();
            U1 = new Vector3d();
            U2 = new Vector3d();
            U3 = new Vector3d();
            v = 0.0;
            Direction = 0;
            Inplane = true;
        }

        public Tri3Element(Point3d p1, Point3d p2, Point3d p3,
                           Vector3d u1, Vector3d u2, Vector3d u3,
                           double poissons, bool inplane)
        {
            Inplane = inplane;

            ElementPlane = new Plane(p1, p2 - p1, p3 - p1);

            ElementPlane.RemapToPlaneSpace(p1, out P1);
            ElementPlane.RemapToPlaneSpace(p2, out P2);
            ElementPlane.RemapToPlaneSpace(p3, out P3);

            if (Inplane)
            {
                U1 = OrientVector(u1, ElementPlane, Plane.WorldXY);
                U2 = OrientVector(u2, ElementPlane, Plane.WorldXY);
                U3 = OrientVector(u3, ElementPlane, Plane.WorldXY);
            }
            else
            {
                U1 = MathForm(OrientVector(u1, ElementPlane, Plane.WorldXY));
                U2 = MathForm(OrientVector(u2, ElementPlane, Plane.WorldXY));
                U3 = MathForm(OrientVector(u3, ElementPlane, Plane.WorldXY));
            }

            v = poissons;
            Direction = 0;
        }

        public void ChangeDirection(int dir)
        {
            Direction = dir;
        }

        public Point3d CalculateNaturalCoordinate(Point3d cartesianPoint)
        {
            Point3d carte1 = P1;
            Point3d carte2 = P2;
            Point3d carte3 = P3;
            // Sum of edge vectors is a Vector3d; translate from P1 to get a valid Point3d.
            Point3d carte4 = P1 + ((P2 - P1) + (P3 - P1));

            Point3d natur1 = new Point3d(-1.0, 1.0, 0.0);
            Point3d natur2 = new Point3d(1.0, 1.0, 0.0);
            Point3d natur3 = new Point3d(-1.0, -1.0, 0.0);
            Point3d natur4 = new Point3d(1.0, -1.0, 0.0);

            Point3d carte5 = new Point3d();
            Point3d carte6 = new Point3d();
            Point3d natur5 = new Point3d();
            Point3d natur6 = new Point3d();

            PolylineCurve partA;

            int side = 0;

            for (int i = 0; i < 16; i++)
            {
                if (side == 0)
                {
                    natur5 = new Point3d((natur1.X + natur2.X) / 2, (natur1.Y + natur2.Y) / 2, 0.0);
                    natur6 = new Point3d((natur3.X + natur4.X) / 2, (natur3.Y + natur4.Y) / 2, 0.0);
                    carte5 = CartesianCoordinates(natur5);
                    carte6 = CartesianCoordinates(natur6);

                    partA = new PolylineCurve(new[] { carte1, carte5, carte6, carte3, carte1 });

                    int testContainment = (int)partA.Contains(
                        cartesianPoint,
                        new Plane(Point3d.Origin, Vector3d.ZAxis),
                        0.00001);

                    if (testContainment == 1 || testContainment == 3)
                    {
                        natur2 = natur5;
                        natur4 = natur6;
                        carte2 = carte5;
                        carte4 = carte6;
                    }
                    else
                    {
                        natur1 = natur5;
                        natur3 = natur6;
                        carte1 = carte5;
                        carte3 = carte6;
                    }

                    side = 1;
                }
                else
                {
                    natur5 = new Point3d((natur1.X + natur3.X) / 2, (natur1.Y + natur3.Y) / 2, 0.0);
                    natur6 = new Point3d((natur2.X + natur4.X) / 2, (natur2.Y + natur4.Y) / 2, 0.0);
                    carte5 = CartesianCoordinates(natur5);
                    carte6 = CartesianCoordinates(natur6);

                    partA = new PolylineCurve(new[] { carte1, carte2, carte6, carte5, carte1 });

                    int testContainment = (int)partA.Contains(
                        cartesianPoint,
                        new Plane(Point3d.Origin, Vector3d.ZAxis),
                        0.00001);

                    if (testContainment == 1 || testContainment == 3)
                    {
                        natur3 = natur5;
                        natur4 = natur6;
                        carte3 = carte5;
                        carte4 = carte6;
                    }
                    else
                    {
                        natur1 = natur5;
                        natur2 = natur6;
                        carte1 = carte5;
                        carte2 = carte6;
                    }

                    side = 0;
                }
            }

            return (natur1 + natur2 + natur3 + natur4) / 4;
        }

        public Point3d CartesianCoordinates(Point3d naturalPoint)
        {
            NaturalCoordinate = naturalPoint;
            CalculateShapeFunctionValues();

            double x = N1 * P1.X + N2 * P2.X + N3 * P3.X;
            double y = N1 * P1.Y + N2 * P2.Y + N3 * P3.Y;
            return new Point3d(x, y, 0.0);
        }

        private void CalculateJacobianTerms()
        {
            Dxdxi = N1xi * P1.X + N2xi * P2.X + N3xi * P3.X;
            Dxdeta = N1eta * P1.X + N2eta * P2.X + N3eta * P3.X;
            Dydxi = N1xi * P1.Y + N2xi * P2.Y + N3xi * P3.Y;
            Dydeta = N1eta * P1.Y + N2eta * P2.Y + N3eta * P3.Y;
        }

        private void CalculateJacobianDeterminant()
        {
            DetJ = Dydeta * Dxdxi - Dydxi * Dxdeta;
        }

        private void CalculateShapeFunctionValues()
        {
            N1 = -0.5 * _xi + 0.5 * _eta;
            N2 = 0.5 * _xi + 0.5;
            N3 = -0.5 * _eta + 0.5;
        }

        private void CalculateDifferentiatedNaturalShapeFunctionValues()
        {
            N1xi = -0.5;
            N1eta = 0.5;
            N2xi = 0.5;
            N2eta = 0;
            N3xi = 0;
            N3eta = -0.5;
        }

        private void CalculateDifferentiatedCartesianShapeFunctionValues()
        {
            CalculateDifferentiatedNaturalShapeFunctionValues();
            CalculateJacobianTerms();
            CalculateJacobianDeterminant();

            N1x = 1 / DetJ * (Dydeta * N1xi - Dydxi * N1eta);
            N1y = 1 / DetJ * (-Dxdeta * N1xi + Dxdxi * N1eta);

            N2x = 1 / DetJ * (Dydeta * N2xi - Dydxi * N2eta);
            N2y = 1 / DetJ * (-Dxdeta * N2xi + Dxdxi * N2eta);

            N3x = 1 / DetJ * (Dydeta * N3xi - Dydxi * N3eta);
            N3y = 1 / DetJ * (-Dxdeta * N3xi + Dxdxi * N3eta);
        }

        public Vector3d Evaluate(Point3d loc)
        {
            Point3d location;
            ElementPlane.RemapToPlaneSpace(loc, out location);

            NaturalCoordinate = CalculateNaturalCoordinate(location);
            CalculateDifferentiatedCartesianShapeFunctionValues();
            CalculateStrains();
            CalculateStresses();
            CalculateTheta();

            if (Direction == 1)
                return OrientVector(new Vector3d(Math.Cos(Theta), Math.Sin(Theta), 0), Plane.WorldXY, ElementPlane);
            if (Direction == 2)
                return OrientVector(new Vector3d(-Math.Sin(Theta), Math.Cos(Theta), 0), Plane.WorldXY, ElementPlane);

            return Vector3d.Zero;
        }

        private void CalculateStrains()
        {
            EpsilonX = N1x * U1.X + N2x * U2.X + N3x * U3.X;
            EpsilonY = N1y * U1.Y + N2y * U2.Y + N3y * U3.Y;
            GammaXY =
                N1y * U1.X + N2y * U2.X + N3y * U3.X +
                N1x * U1.Y + N2x * U2.Y + N3x * U3.Y;
        }

        private void CalculateStresses()
        {
            SigmaX = EpsilonX + v * EpsilonY;
            SigmaY = EpsilonY + v * EpsilonX;
            TauXY = (1 - v) / 2 * GammaXY;
        }

        private void CalculateTheta()
        {
            if (SigmaX == SigmaY)
            {
                Theta = 0.0;
            }
            else
            {
                Theta = Math.Atan(2 * TauXY / (SigmaX - SigmaY)) / 2;

                if (SigmaY > (SigmaX + SigmaY) / 2)
                {
                    if (TauXY > 0) Theta -= Math.PI / 2;
                    else Theta += Math.PI / 2;
                }
            }
        }

        private Vector3d OrientVector(Vector3d vector, Plane plane0, Plane plane1)
        {
            Transform orient = Transform.PlaneToPlane(plane0, plane1);
            vector.Transform(orient);
            return vector;
        }

        private Vector3d MathForm(Vector3d vector)
        {
            return new Vector3d(vector.Y, -vector.X, 0.0);
        }
    }

    /// <summary>
    /// Thin wrapper that can hold either a quad or tri element for evaluation.
    /// </summary>
    class PrincipalElement
    {
        private int _type; // 3 = Tri3Element, 4 = Quad4Element
        private Tri3Element _tri3;
        private Quad4Element _quad4;

        public PrincipalElement(Quad4Element quad4)
        {
            _type = 4;
            _quad4 = quad4;
        }

        public PrincipalElement(Tri3Element tri3)
        {
            _type = 3;
            _tri3 = tri3;
        }

        public Vector3d Evaluate(Point3d location)
        {
            if (_type == 4) return _quad4.Evaluate(location);
            if (_type == 3) return _tri3.Evaluate(location);
            return Vector3d.Zero;
        }
    }

    /// <summary>
    /// Field mesh wrapping a Rhino mesh and associated principal-direction elements.
    /// </summary>
    class FieldMesh
    {
        public Mesh Mesh;
        public Polyline[] NakedEdges;
        public Plane MeshPlane;
        private readonly List<PrincipalElement> _elements;

        public FieldMesh()
        {
            Mesh = new Mesh();
            NakedEdges = Array.Empty<Polyline>();
            _elements = new List<PrincipalElement>();
            MeshPlane = Plane.WorldXY;
        }

        public FieldMesh(List<PrincipalElement> elements, Mesh mesh)
        {
            Mesh = mesh;
            _elements = elements;
            NakedEdges = Mesh.GetNakedEdges();

            if (Mesh.Vertices.Count >= 3)
                MeshPlane = new Plane(Mesh.Vertices[0], Mesh.Vertices[1], Mesh.Vertices[2]);
            else
                MeshPlane = Plane.WorldXY;
        }

        public bool Evaluate(Point3d location, ref Vector3d direction)
        {
            // Find which face the point is on
            MeshPoint closestPoint = Mesh.ClosestMeshPoint(location, 0.0);
            location = closestPoint.Point;

            if (closestPoint.FaceIndex < 0 || closestPoint.FaceIndex >= _elements.Count)
            {
                direction = Vector3d.Zero;
                return false;
            }

            direction = _elements[closestPoint.FaceIndex].Evaluate(location);
            return true;
        }
    }

    /// <summary>
    /// Wrapper for a principal field mesh used by the streamline integrator.
    /// </summary>
    class PrincipalMesh
    {
        private readonly int _type; // 2 = FieldMesh (from original LilyPad convention)
        public FieldMesh FieldMesh;
        public Mesh Mesh;
        public Polyline[] NakedEdges;

        public PrincipalMesh(FieldMesh fieldMesh)
        {
            _type = 2;
            FieldMesh = fieldMesh;
            Mesh = fieldMesh.Mesh;
            NakedEdges = fieldMesh.NakedEdges;
        }

        public bool Evaluate(Point3d location, ref Vector3d vector)
        {
            if (_type == 2) return FieldMesh.Evaluate(location, ref vector);
            return false;
        }
    }

    /// <summary>
    /// Streamline generator on a principal mesh.
    /// This is a trimmed version of LilyPad's Streamlines class that only supports
    /// the neighbour seeding strategy (no Triangle.NET dependency).
    /// </summary>
    class Streamlines
    {
        // Principal field
        private readonly PrincipalMesh _principalMesh;
        public Mesh Mesh;
        private readonly Polyline[] _nakedEdges;

        // Integration state
        private Point3d _point;
        private Point3d _end;
        private Point3d _nextPoint;
        private double _error;
        private double _nextError;
        private Vector3d _vec0;
        private Vector3d _vec1;
        private double _stepSize;
        private readonly double _initialStepSize;
        private readonly int _method;
        private readonly double _maxError;
        private readonly double _dTest;

        // Seeding
        private int _seedingStrategy;
        private double _dSep;

        // Storage
        public List<Point3d> UsedSeeds;
        private Polyline _streamline;
        private List<Polyline> _completedStreamlines;
        private List<Point3d> _checkPts;

        public Streamlines()
        {
            _principalMesh = new PrincipalMesh(new FieldMesh());
            Mesh = _principalMesh.Mesh;
            _nakedEdges = _principalMesh.NakedEdges;
            _completedStreamlines = new List<Polyline>();
            UsedSeeds = new List<Point3d>();
            _checkPts = new List<Point3d>();
            _initialStepSize = 0.1;
            _stepSize = _initialStepSize;
            _method = 4;
            _maxError = 0.0;
            _dTest = 0.0;
        }

        public Streamlines(PrincipalMesh principalMesh, double stepSize, int method, double maxError, double dTest)
        {
            _principalMesh = principalMesh;
            Mesh = principalMesh.Mesh;
            Mesh.FaceNormals.ComputeFaceNormals();
            _nakedEdges = principalMesh.NakedEdges;

            _completedStreamlines = new List<Polyline>();
            UsedSeeds = new List<Point3d>();
            _checkPts = new List<Point3d>();

            _initialStepSize = stepSize;
            _stepSize = stepSize;
            _method = method;
            _maxError = maxError;
            _dTest = dTest;
        }

        public Polyline CreateStreamline(Point3d seed)
        {
            _streamline = new Polyline();

            _point = seed;
            _stepSize = _initialStepSize;

            Polyline segment1 = CreateStreamlineSegment(1);
            if (segment1.IsClosed) return segment1;

            _point = seed;
            _stepSize = _initialStepSize;

            Polyline segment2 = CreateStreamlineSegment(-1);
            if (segment2.IsClosed) return segment2;

            segment2.RemoveAt(0);
            segment2.Reverse();
            segment1.InsertRange(0, segment2);

            return segment1;
        }

        public List<Polyline> CreateStreamlines(Point3d seed, int seedingMethod, double dSep)
        {
            _seedingStrategy = seedingMethod;
            _dSep = dSep;

            _completedStreamlines = new List<Polyline>();
            UsedSeeds = new List<Point3d>();
            _checkPts = new List<Point3d>();

            if (seedingMethod == 1)
            {
                NeighbourStreamlines(seed);
            }
            else
            {
                throw new NotImplementedException("Only neighbour seeding (method=1) is implemented.");
            }

            return _completedStreamlines;
        }

        private void NeighbourStreamlines(Point3d seed)
        {
            var seeds = new List<Point3d> { seed };
            Polyline activeStreamline = new Polyline();

            while (seeds.Count > 0)
            {
                seed = seeds[0];

                // Enforce separation distance to existing check points
                Vector3d radius1 = new Vector3d(_dSep * 0.99, _dSep * 0.99, _dSep * 0.99);
                BoundingBox testBox = new BoundingBox(seed - radius1, seed + radius1);
                for (int i = 0; i < _checkPts.Count; i++)
                {
                    if (testBox.Contains(_checkPts[i]))
                    {
                        if ((_checkPts[i] - seed).Length < _dSep * 0.99)
                        {
                            seeds.RemoveAt(0);
                            goto BREAK;
                        }
                    }
                }

                activeStreamline = CreateStreamline(seed);

                if (activeStreamline.Count < 3)
                {
                    seeds.RemoveAt(0);
                    goto BREAK;
                }

                _completedStreamlines.Add(activeStreamline);

                // Add vertices to check list
                for (int i = 0; i < activeStreamline.Count; i++)
                {
                    _checkPts.Add(activeStreamline[i]);
                }

                UsedSeeds.Add(seed);
                seeds.RemoveAt(0);

                // New seeds either side of streamline
                for (int i = 0; i < activeStreamline.Count - 1; i += 10)
                {
                    Point3d pt = activeStreamline[i];
                    Point3d ptPlus1 = activeStreamline[i + 1];
                    Vector3d streamlineDirection = ptPlus1 - pt;

                    MeshPoint meshPt = Mesh.ClosestMeshPoint(pt, 0.0);

                    streamlineDirection.Rotate(Math.PI / 2, Mesh.FaceNormals[meshPt.FaceIndex]);
                    Point3d seed1 = pt + streamlineDirection / streamlineDirection.Length * _dSep;
                    streamlineDirection.Rotate(Math.PI, Mesh.FaceNormals[meshPt.FaceIndex]);
                    Point3d seed2 = pt + streamlineDirection / streamlineDirection.Length * _dSep;

                    seed1 = Mesh.ClosestPoint(seed1);
                    seed2 = Mesh.ClosestPoint(seed2);

                    seeds.Insert(0, seed1);
                    seeds.Insert(0, seed2);
                }

            BREAK:;
            }
        }

        private Polyline CreateStreamlineSegment(int direction)
        {
            Polyline streamlineSegment = new Polyline();

            bool keepGoing = true;
            int level = 0;
            int track1 = -2;

            _vec0 = Vector3d.Zero;
            _vec1 = Vector3d.Zero;

            streamlineSegment.Add(_point);
            _error = SolveStep(_point, out _end, _method, direction);
            _vec0 = _end - _point;
            _vec0.Unitize();
            Vector3d vecFirst = _vec0 * direction;

            int i = 0;
            while (keepGoing)
            {
                _end = Mesh.ClosestPoint(_end);

                keepGoing = true;
                foreach (var edge in _nakedEdges)
                {
                    Point3d testPoint = edge.ClosestPoint(_end);
                    double dist = testPoint.DistanceTo(_end);
                    if (dist < 0.001)
                    {
                        keepGoing = false;
                        _error = SolveStep(_point, out _end, _method, direction);
                        Line edgeSegment = new Line(_point, _end);
                        edge.ToNurbsCurve().ClosestPoints(edgeSegment.ToNurbsCurve(), out _end, out Point3d _);
                    }
                }

                if (!keepGoing && _point.DistanceTo(_end) > 0.0001)
                    streamlineSegment.Add(_end);

                if (keepGoing)
                {
                    _nextError = SolveStep(_end, out _nextPoint, _method, direction);
                    _vec1 = _nextPoint - _end;
                    _vec1.Unitize();

                    double angle = Math.Abs(Vector3d.VectorAngle(_vec0, _vec1));

                    if (_maxError > 0)
                    {
                        if ((_error > _maxError || angle > 0.1 * Math.PI) && level < 7)
                        {
                            _stepSize /= 2;
                            level++;
                            track1 = i;
                            _error = SolveStep(_point, out _end, _method, direction);
                            _vec0 = _end - _point;
                            _vec0.Unitize();
                        }
                        else if (_error < _maxError / 2 && angle < 0.05 * Math.PI && level > 0 && track1 + 1 != i)
                        {
                            _stepSize *= 2;
                            level--;
                            _error = SolveStep(_point, out _end, _method, direction);
                            _vec0 = _end - _point;
                            _vec0.Unitize();
                        }
                        else if (angle > Math.PI * 0.7)
                        {
                            if (level == 7)
                            {
                                direction *= -1;
                                _vec1 = -_vec1;
                                _nextError = SolveStep(_end, out _nextPoint, _method, direction);
                                keepGoing = AddInNewPoint(streamlineSegment);
                            }
                            else
                            {
                                _stepSize /= 2;
                                level++;
                                track1 = i;
                                _error = SolveStep(_point, out _end, _method, direction);
                                _vec0 = _end - _point;
                                _vec0.Unitize();
                            }
                        }
                        else
                        {
                            keepGoing = AddInNewPoint(streamlineSegment);
                        }
                    }
                    else
                    {
                        if (angle > Math.PI * 0.7)
                        {
                            direction *= -1;
                            _vec1 = -_vec1;
                            _nextError = SolveStep(_end, out _nextPoint, _method, direction);
                        }

                        keepGoing = AddInNewPoint(streamlineSegment);
                    }

                    if (streamlineSegment.Length > 3 * _initialStepSize &&
                        Math.Abs(Vector3d.VectorAngle(vecFirst, _vec1)) < 0.1 * Math.PI &&
                        _end.DistanceTo(streamlineSegment[0]) < _initialStepSize)
                    {
                        streamlineSegment.Add(streamlineSegment[0]);
                        return streamlineSegment;
                    }
                    else if (streamlineSegment.Length > 3 * _initialStepSize && streamlineSegment.Count >= 10)
                    {
                        int rangeStart = streamlineSegment.Count - 10;
                        for (int j = rangeStart; j >= 0; j--)
                        {
                            double dist = streamlineSegment[j].DistanceTo(_end);
                            if (dist < _stepSize)
                            {
                                Vector3d vecJ = new Vector3d(streamlineSegment[j + 1] - streamlineSegment[j]);
                                if (Math.Abs(Vector3d.VectorAngle(vecJ, _vec0)) < 0.1 * Math.PI)
                                {
                                    streamlineSegment.RemoveRange(0, j);
                                    streamlineSegment.Add(streamlineSegment[0]);
                                    return streamlineSegment;
                                }
                            }
                        }
                    }
                }

                if (i > 10000) keepGoing = false;
                i++;
            }

            return streamlineSegment;
        }

        private bool AddInNewPoint(Polyline polyline)
        {
            bool dTestOk;
            if (_dTest > 0)
            {
                dTestOk = CheckDTest(_end);
                if (dTestOk)
                {
                    polyline.Add(_end);
                    _error = _nextError;
                    _point = _end;
                    _end = _nextPoint;
                    _vec0 = _vec1;
                    return true;
                }
                return false;
            }

            polyline.Add(_end);
            _error = _nextError;
            _point = _end;
            _end = _nextPoint;
            _vec0 = _vec1;
            return true;
        }

        private bool CheckDTest(Point3d point)
        {
            BoundingBox testBox = new BoundingBox(
                point - new Vector3d(_dTest * 0.99, _dTest * 0.99, _dTest * 0.99),
                point + new Vector3d(_dTest * 0.99, _dTest * 0.99, _dTest * 0.99));

            for (int i = 0; i < _checkPts.Count; i++)
            {
                if (testBox.Contains(_checkPts[i]))
                {
                    if (_checkPts[i].DistanceTo(point) > _dTest) return false;
                }
            }

            return true;
        }

        private Vector3d Evaluate(Point3d point)
        {
            Vector3d vector = new Vector3d();
            _principalMesh.Evaluate(point, ref vector);
            return vector;
        }

        private double SolveStep(Point3d start, out Point3d end, int method, int direction)
        {
            if (method != 4)
                throw new NotImplementedException("Only Runge-Kutta 4 integration (method=4) is implemented.");

            return SolveRK4(start, out end, direction);
        }

        private double SolveRK4(Point3d start, out Point3d end, int direction)
        {
            double angle;

            Vector3d vectorAtStart = Evaluate(start);
            Vector3d vectorFromStart = vectorAtStart / vectorAtStart.Length * _stepSize * direction;

            Point3d point1 = start + vectorFromStart / 2;
            Vector3d vectorAtPoint1 = Evaluate(point1);
            angle = Math.Abs(Vector3d.VectorAngle(vectorAtStart, vectorAtPoint1));
            if (angle > Math.PI * 0.75) vectorAtPoint1 = -vectorAtPoint1;
            Vector3d vectorFromPoint1 = vectorAtPoint1 / vectorAtPoint1.Length * _stepSize * direction;

            Point3d point2 = start + vectorFromPoint1 / 2;
            Vector3d vectorAtPoint2 = Evaluate(point2);
            angle = Math.Abs(Vector3d.VectorAngle(vectorAtStart, vectorAtPoint2));
            if (angle > Math.PI * 0.75) vectorAtPoint2 = -vectorAtPoint2;
            Vector3d vectorFromPoint2 = vectorAtPoint2 / vectorAtPoint2.Length * _stepSize * direction;

            Point3d point3 = start + vectorFromPoint2;
            Vector3d vectorAtPoint3 = Evaluate(point3);
            angle = Math.Abs(Vector3d.VectorAngle(vectorAtStart, vectorAtPoint3));
            if (angle > Math.PI * 0.75) vectorAtPoint3 = -vectorAtPoint3;
            Vector3d vectorFromPoint3 = vectorAtPoint3 / vectorAtPoint3.Length * _stepSize * direction;

            end = start + (vectorFromStart + 2 * vectorFromPoint1 + 2 * vectorFromPoint2 + vectorFromPoint3) / 6;
            end = Mesh.ClosestPoint(end);

            Vector3d vectorAtEnd = Evaluate(end);
            angle = Math.Abs(Vector3d.VectorAngle(vectorAtStart, vectorAtEnd));
            if (angle > Math.PI * 0.75) vectorAtEnd = -vectorAtEnd;
            Vector3d vectorFromEnd = vectorAtEnd / vectorAtEnd.Length * _stepSize * direction;

            return ((vectorFromPoint3 - vectorFromEnd) / 6).Length;
        }
    }
}


