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
        // Build a level-30 cell ID following the reference Google S2 algorithm.
        // The position is accumulated in two 64-bit halves.
        // n[1] is initialized with the face, later shifted left by 32.
        var n0 = 0UL;
        var n1 = (ulong)face;

        int bits = face & SwapMask;
        int mask = (1 << LookupBits) - 1; // 0xF

        for (int k = 7; k >= 0; k--)
        {
            bits += ((i >> (k * LookupBits)) & mask) << (LookupBits + 2);
            bits += ((j >> (k * LookupBits)) & mask) << 2;
            bits = LookupPos[bits];

            // Store 8 position bits into the appropriate half.
            if (k >= 4)
            {
                n1 |= (ulong)(bits >> 2) << (((k & 3) * 2 * LookupBits));
            }
            else
            {
                n0 |= (ulong)(bits >> 2) << (((k & 3) * 2 * LookupBits));
            }

            bits &= (SwapMask | InvertMask);
        }

        n1 <<= 32;
        long cellId = (long)((n1 | n0) * 2 + 1);

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
        InitLookupCell(0, 0, 0, 0, 0, 0);
        InitLookupCell(0, 0, 0, 0, 0, 1);
        InitLookupCell(0, 0, 0, 0, 0, 2);
        InitLookupCell(0, 0, 0, 0, 0, 3);
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

        // The Hilbert curve subcell traversal order (in ij-space) and orientation
        // transitions. posToIj maps position (0-3) to the (i,j) offset pair encoded
        // as a 2-bit value: bit1 = di, bit0 = dj.
        ReadOnlySpan<int> posToIj =
        [
            0, 1, 3, 2,
        ];

        // posToOrientation[orientation * 4 + pos] gives the child orientation.
        ReadOnlySpan<int> posToOrientation =
        [
            1, 0, 0, 3,
            0, 1, 1, 2,
            3, 2, 2, 1,
            2, 3, 3, 0,
        ];

        for (int s = 0; s < 4; s++)
        {
            int ij = posToIj[s];

            if ((orientation & SwapMask) != 0)
            {
                ij = ((ij & 1) << 1) | ((ij >> 1) & 1);
            }

            if ((orientation & InvertMask) != 0)
            {
                ij ^= 3;
            }

            int di = (ij >> 1) & 1;
            int dj = ij & 1;

            int newOrientation = posToOrientation[(orientation * 4) + s];

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
