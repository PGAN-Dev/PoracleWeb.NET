namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Computes S2 cell IDs from lat/lon coordinates.
/// Pokemon GO uses S2 level-10 cells (~100 km2) for weather.
/// </summary>
public static class S2CellHelper
{
    private const int MaxLevel = 30;
    private const int LookupBits = 4;

    // Lookup tables for Hilbert curve (i,j) -> position mapping.
    // Index: (iChunk << (LookupBits+2)) | (jChunk << 2) | orientation
    // Value: (posChunk << 2) | newOrientation
    private static readonly int[] LookupPos = new int[1024];

    // Orientation constants matching the S2 library.
    private const int SwapMask = 1;
    private const int InvertMask = 2;

    static S2CellHelper()
    {
        InitLookupTables();
    }

    /// <summary>
    /// Computes the S2 cell ID at the given level for the specified lat/lon in degrees.
    /// </summary>
    public static long LatLonToCellId(double latDeg, double lonDeg, int level)
    {
        var latRad = latDeg * Math.PI / 180.0;
        var lonRad = lonDeg * Math.PI / 180.0;

        // Convert to XYZ on unit sphere.
        var cosLat = Math.Cos(latRad);
        var x = cosLat * Math.Cos(lonRad);
        var y = cosLat * Math.Sin(lonRad);
        var z = Math.Sin(latRad);

        // Determine face and (u, v) projection.
        var (face, u, v) = XyzToFaceUv(x, y, z);

        // Apply quadratic ST transform.
        var s = UvToSt(u);
        var t = UvToSt(v);

        // Discretize to (i, j) on a 2^30 grid.
        var si = StToSiTi(s);
        var ti = StToSiTi(t);

        // Clamp to valid range [0, 2^30 - 1].
        var i = Math.Clamp((int)(si >> 1), 0, (1 << MaxLevel) - 1);
        var j = Math.Clamp((int)(ti >> 1), 0, (1 << MaxLevel) - 1);

        // Build cell ID from face + Hilbert curve position at level 30, then truncate.
        return FaceIjToCell(face, i, j, level);
    }

    /// <summary>
    /// Convenience method: returns the S2 level-10 cell ID used by Pokemon GO for weather.
    /// </summary>
    public static long LatLonToWeatherCellId(double lat, double lon)
    {
        return LatLonToCellId(lat, lon, 10);
    }

    private static (int face, double u, double v) XyzToFaceUv(double x, double y, double z)
    {
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var absZ = Math.Abs(z);

        int face;
        double u, v;

        if (absX >= absY && absX >= absZ)
        {
            if (x > 0)
            {
                face = 0;
                u = y / x;
                v = z / x;
            }
            else
            {
                face = 3;
                u = z / x;
                v = y / x;
            }
        }
        else if (absY >= absX && absY >= absZ)
        {
            if (y > 0)
            {
                face = 1;
                u = -x / y;
                v = z / y;
            }
            else
            {
                face = 4;
                u = z / y;
                v = -x / y;
            }
        }
        else
        {
            if (z > 0)
            {
                face = 2;
                u = -x / z;
                v = -y / z;
            }
            else
            {
                face = 5;
                u = -y / z;
                v = -x / z;
            }
        }

        return (face, u, v);
    }

    private static double UvToSt(double u)
    {
        return u >= 0
            ? 0.5 * Math.Sqrt(1.0 + 3.0 * u)
            : 1.0 - 0.5 * Math.Sqrt(1.0 - 3.0 * u);
    }

    private static uint StToSiTi(double s)
    {
        // Convert ST to an unsigned integer in [0, 2^31].
        // The result is twice the (i or j) value so that the center of leaf cells
        // corresponds to odd values.
        return (uint)Math.Max(0, Math.Min((1L << 31) - 1, (long)Math.Round(s * (1L << 31))));
    }

    private static long FaceIjToCell(int face, int i, int j, int level)
    {
        // Build a level-30 cell ID following the Go S2 library approach
        // (golang/geo s2/cellid.go). Face is placed at bit 60 upfront,
        // then position bits are OR'd into the lower bits.
        // After n*2+1, face moves to bits 63-61 and sentinel is at bit 0.
        const int posBits = 2 * MaxLevel + 1; // 61
        var n = (ulong)face << (posBits - 1); // face << 60

        int bits = face & SwapMask;
        int mask = (1 << LookupBits) - 1; // 0xF

        for (int k = 7; k >= 0; k--)
        {
            bits += ((i >> (k * LookupBits)) & mask) << (LookupBits + 2);
            bits += ((j >> (k * LookupBits)) & mask) << 2;
            bits = LookupPos[bits];
            n |= (ulong)(bits >> 2) << (k * 2 * LookupBits);
            bits &= (SwapMask | InvertMask);
        }

        long cellId = (long)(n * 2 + 1);

        // Truncate to requested level: keep face + 2*level position bits + sentinel.
        if (level < MaxLevel)
        {
            int shift = 2 * (MaxLevel - level);
            cellId = (cellId & (~0L << (shift + 1))) | (1L << shift);
        }

        return cellId;
    }

    private static void InitLookupTables()
    {
        // origOrientation must match orientation (reference: Go S2 cellid.go init())
        InitLookupCell(0, 0, 0, 0, 0, 0);
        InitLookupCell(0, 0, 0, SwapMask, 0, SwapMask);
        InitLookupCell(0, 0, 0, InvertMask, 0, InvertMask);
        InitLookupCell(0, 0, 0, SwapMask | InvertMask, 0, SwapMask | InvertMask);
    }

    private static void InitLookupCell(
        int level, int i, int j, int origOrientation, int pos, int orientation)
    {
        if (level == LookupBits)
        {
            int ijIndex = (i << (LookupBits + 2)) | (j << 2) | origOrientation;
            LookupPos[ijIndex] = (pos << 2) | orientation;
            return;
        }

        level++;

        // Pre-computed Hilbert curve subcell traversal orders derived from the
        // reference Google S2 library's kIJtoPos table (s2cell_id.cc). These are
        // the INVERSE mapping: kPosToIJ[orientation][pos] gives the (i,j) offset
        // encoded as bit1=di, bit0=dj.
        ReadOnlySpan<int> posToIj =
        [
            // orientation 0: canonical
            0, 1, 3, 2,
            // orientation 1: swap
            0, 2, 3, 1,
            // orientation 2: invert
            3, 2, 0, 1,
            // orientation 3: swap+invert
            3, 1, 0, 2,
        ];

        // kPosToOrientation[pos] is XOR'd with the current orientation to get
        // the child orientation.
        ReadOnlySpan<int> posToOrientationDelta =
        [
            SwapMask, 0, 0, SwapMask | InvertMask,
        ];

        for (int s = 0; s < 4; s++)
        {
            int ij = posToIj[(orientation * 4) + s];
            int di = (ij >> 1) & 1;
            int dj = ij & 1;

            int newOrientation = orientation ^ posToOrientationDelta[s];

            InitLookupCell(
                level,
                (i << 1) + di,
                (j << 1) + dj,
                origOrientation,
                (pos << 2) + s,
                newOrientation);
        }
    }
}
