﻿using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;

namespace ToolCore.Utils
{
    internal class Draw
    {
        internal static readonly MyStringId _square = MyStringId.GetOrCompute("Square");

        internal static void DrawLocalVector(Vector3 local, MyEntity entity, MyEntity parent, Color color, float length = 5f)
        {
            Vector3 world;
            var matrix = (Matrix)parent.PositionComp.WorldMatrixRef;
            matrix.Translation += entity.PositionComp.LocalMatrixRef.Translation;
            Vector3.TransformNormal(ref local, ref matrix, out world);
            DrawLine(matrix.Translation, world, color, 0.05f, length);
        }

        internal static void DrawBox(MyOrientedBoundingBoxD obb, Color color, bool solid = true, int divideRatio = 20, float lineWidth = 0.02f)
        {
            var box = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);
            var wm = MatrixD.CreateFromTransformScale(obb.Orientation, obb.Center, Vector3D.One);
            var raster = solid ? MySimpleObjectRasterizer.Solid : MySimpleObjectRasterizer.Wireframe;
            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, divideRatio, lineWidth, null, _square);
        }

        internal static void DrawCylinder(MatrixD world, float radius, float length, Color color)
        {
            var c = (Vector4)color;
            MySimpleObjectDraw.DrawTransparentCylinder(ref world, radius, radius, length, ref c, false, 16, 0.02f, _square);
        }

        internal static void DrawSphere(BoundingSphereD sphere, Color color, bool solid = true, int divideRatio = 20, float lineWidth = 0.02f)
        {
            DrawScaledPoint(sphere.Center, sphere.Radius, color, solid, divideRatio, lineWidth);
        }

        internal static void DrawSphere(MatrixD drawMatrix, double radius, Color color, bool solid = true, int divideRatio = 20, float lineWidth = 0.02f)
        {
            MatrixD.Rescale(ref drawMatrix, radius);
            var raster = solid ? MySimpleObjectRasterizer.Solid : MySimpleObjectRasterizer.Wireframe;
            MySimpleObjectDraw.DrawTransparentSphere(ref drawMatrix, 1f, ref color, raster, divideRatio, null, _square, lineWidth);
        }

        internal static void DrawScaledPoint(Vector3D pos, double radius, Color color, bool solid = true, int divideRatio = 20, float lineWidth = 0.02f)
        {
            var posMatCenterScaled = MatrixD.CreateTranslation(pos);
            var posMatScaler = MatrixD.Rescale(posMatCenterScaled, radius);
            var raster = solid ? MySimpleObjectRasterizer.Solid : MySimpleObjectRasterizer.Wireframe;
            MySimpleObjectDraw.DrawTransparentSphere(ref posMatScaler, 1f, ref color, raster, divideRatio, null, _square, lineWidth);
        }

        internal static void DrawLine(Vector3D start, Vector3D end, Color color, float width)
        {
            var c = (Vector4)color;
            MySimpleObjectDraw.DrawLine(start, end, _square, ref c, width);
        }

        internal static void DrawLine(Vector3D start, Vector3D dir, Color color, float width, float length)
        {
            var c = (Vector4)color;
            MySimpleObjectDraw.DrawLine(start, start + (dir * length), _square, ref c, width);
        }

        internal static void DrawLine(Vector3D start, Vector3D end, Vector4 color, float width, MyStringId material)
        {
            var c = (Vector4)color;
            MySimpleObjectDraw.DrawLine(start, end, material, ref c, width);
        }
    }
}
