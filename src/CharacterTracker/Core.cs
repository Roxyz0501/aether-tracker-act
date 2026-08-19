using System;

namespace CharacterTracker
{
    public sealed class ActorSnapshot
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int Type { get; set; }
        public string Kind { get; set; }
        public int Job { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public uint CurrentHp { get; set; }
        public uint MaxHp { get; set; }
        public uint OwnerId { get; set; }
        public uint TargetId { get; set; }
        public bool IsPlayer { get; set; }
    }

    public sealed class MapCalibration
    {
        public uint TerritoryId { get; set; }
        public double XFactor { get; set; }
        public double XOffset { get; set; }
        public double YFactor { get; set; }
        public double YOffset { get; set; }

        public double MapX(float worldX) { return worldX * XFactor + XOffset; }
        public double MapY(float worldY) { return worldY * YFactor + YOffset; }

        public static MapCalibration FromTwoPoints(uint territoryId, CalibrationPoint a, CalibrationPoint b)
        {
            double dx = b.WorldX - a.WorldX;
            double dy = b.WorldY - a.WorldY;
            if (Math.Abs(dx) < 5 || Math.Abs(dy) < 5)
                throw new ArgumentException(Localization.T("error.move"));
            var xf = (b.MapX - a.MapX) / dx;
            var yf = (b.MapY - a.MapY) / dy;
            if (Math.Abs(xf) < 0.001 || Math.Abs(xf) > 0.2 || Math.Abs(yf) < 0.001 || Math.Abs(yf) > 0.2)
                throw new ArgumentException(Localization.T("error.scale"));
            return new MapCalibration {
                TerritoryId = territoryId,
                XFactor = xf, XOffset = a.MapX - a.WorldX * xf,
                YFactor = yf, YOffset = a.MapY - a.WorldY * yf
            };
        }
    }

    public sealed class CalibrationPoint
    {
        public uint TerritoryId { get; set; }
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
    }

    public static class TrackingMath
    {
        // FFXIV_ACT_Plugin: X/Y form the horizontal map plane, Z is height.
        // +Y is map south, +X is east, heading 0 faces +Y.
        public static double RelativeDirectionRadians(ActorSnapshot player, ActorSnapshot target, bool playerFacingUp)
        {
            double worldDirection = Math.Atan2(target.X - player.X, target.Y - player.Y);
            return Normalize(playerFacingUp ? worldDirection - player.Heading : worldDirection);
        }

        public static double HorizontalDistance(ActorSnapshot a, ActorSnapshot b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Normalize(double value)
        {
            while (value > Math.PI) value -= Math.PI * 2;
            while (value < -Math.PI) value += Math.PI * 2;
            return value;
        }

        public static string KindName(int type)
        {
            switch (type) {
                case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: return Localization.T("kind." + type);
                default: return Localization.F("kind.other", type);
            }
        }
    }
}
