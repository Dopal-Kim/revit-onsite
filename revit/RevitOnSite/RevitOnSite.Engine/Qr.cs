// QR 코드 만들기 (ISO/IEC 18004 모델 2) — [현장 입력] 창이 휴대폰 접속 주소를 보여 줄 때만 쓴다. 외부 패키지 없이 (CLAUDE: 패키지 의존 금지).
//   바이트 모드, 오류 정정 M, 버전 1–10 (UTF-8 213바이트까지 — 접속 주소는 50바이트 안팎). 구조는 Project Nayuki 의 QR Code generator (MIT) 를 따름.
//   시험: RevitOnSite.Field.Check (리드-솔로몬 알려진 값 + 독립 디코더 jsQR 로 다시 읽기)
using System.Text;

namespace RevitOnSite.Engine;

public static class Qr
{
    static readonly int[] EccPerBlock = { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };   // 오류 정정 M, 버전 1–10
    static readonly int[] Blocks = { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

    /// <summary>글자(UTF-8) → 모듈 [y, x] (true = 검정). 둘레 여백(4칸)은 그리는 쪽에서</summary>
    public static bool[,] Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int ver = 1;
        while (ver <= 10 && 4 + (ver < 10 ? 8 : 16) + data.Length * 8 > DataCodewords(ver) * 8) ver++;
        if (ver > 10) throw new ArgumentException($"QR 에 넣기엔 너무 깁니다 ({data.Length}바이트, 213바이트까지)");

        // 비트열: 모드 0100(바이트), 글자 수, 자료, 끝 표시 0000, 바이트 맞춤, 채움 EC 11 …
        var bits = new List<bool>();
        void Put(int v, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0); }
        Put(4, 4);
        Put(data.Length, ver < 10 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        int cap = DataCodewords(ver) * 8;
        Put(0, Math.Min(4, cap - bits.Count));
        Put(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Put(pad, 8);
        var cw = new byte[cap / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));
        var all = Interleave(cw, ver);

        int size = ver * 4 + 17;
        var m = new bool[size, size];
        var fn = new bool[size, size];   // 기능 패턴 자리 (자료·마스크가 안 들어감)
        void Set(int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; }

        for (int i = 0; i < size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }   // 타이밍
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })            // 찾기 패턴 + 분리선
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (x >= 0 && x < size && y >= 0 && y < size) Set(x, y, d != 2 && d != 4);
                }
        var pos = AlignPos(ver);                                                                // 정렬 패턴 (찾기 패턴 세 모서리 제외)
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        void Format(int mask)   // 형식 정보 (M = 00, 마스크 3비트, BCH 10비트, 0x5412 XOR) 두 벌 + 검정 모듈
        {
            int d = mask, rem = d;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int f = ((d << 10) | rem) ^ 0x5412;
            bool B(int i) => ((f >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(8, i, B(i));
            Set(8, 7, B(6)); Set(8, 8, B(7)); Set(7, 8, B(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, B(i));
            for (int i = 0; i < 8; i++) Set(size - 1 - i, 8, B(i));
            for (int i = 8; i < 15; i++) Set(8, size - 15 + i, B(i));
            Set(8, size - 8, true);
        }
        Format(0);
        if (ver >= 7)   // 버전 정보 (BCH 18비트) 두 벌
        {
            int rem = ver;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int v = (ver << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool b = ((v >> i) & 1) != 0;
                int a = size - 11 + i % 3, c = i / 3;
                Set(a, c, b); Set(c, a, b);
            }
        }

        // 자료 넣기: 오른쪽 아래에서 두 열씩 지그재그 (6열 = 타이밍은 건너뜀)
        int k = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j, y = ((right + 1) & 2) == 0 ? size - 1 - vert : vert;
                    if (!fn[y, x] && k < all.Length * 8) { m[y, x] = ((all[k >> 3] >> (7 - (k & 7))) & 1) != 0; k++; }
                }
        }

        // 마스크 8가지 중 벌점이 가장 낮은 것
        void Apply(int msk)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!fn[y, x] && msk switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (x / 3 + y / 2) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    })
                        m[y, x] = !m[y, x];
        }
        int best = 0, min = int.MaxValue;
        for (int msk = 0; msk < 8; msk++)
        {
            Apply(msk);
            Format(msk);
            int p = Penalty(m, size);
            if (p < min) (min, best) = (p, msk);
            Apply(msk);   // 되돌림 (XOR)
        }
        Apply(best);
        Format(best);
        return m;
    }

    static int RawCodewords(int ver)
    {
        int r = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int n = ver / 7 + 2;
            r -= (25 * n - 10) * n - 55;
            if (ver >= 7) r -= 36;
        }
        return r / 8;
    }

    static int DataCodewords(int ver) => RawCodewords(ver) - EccPerBlock[ver - 1] * Blocks[ver - 1];

    static int[] AlignPos(int ver)
    {
        if (ver == 1) return Array.Empty<int>();
        int n = ver / 7 + 2, size = ver * 4 + 17, step = (ver * 4 + n * 2 + 1) / (n * 2 - 2) * 2;
        var r = new int[n];
        r[0] = 6;
        for (int i = n - 1, p = size - 7; i >= 1; i--, p -= step) r[i] = p;
        return r;
    }

    /// <summary>블록마다 리드-솔로몬 오류 정정을 붙이고 엇갈려 늘어놓음 (짧은 블록은 자료 끝 한 칸이 빔)</summary>
    static byte[] Interleave(byte[] data, int ver)
    {
        int nb = Blocks[ver - 1], ecc = EccPerBlock[ver - 1], raw = RawCodewords(ver);
        int nShort = nb - raw % nb, shortLen = raw / nb;
        var div = RsDivisor(ecc);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < nb; i++)
        {
            int len = shortLen - ecc + (i < nShort ? 0 : 1);
            var dat = data[k..(k + len)];
            k += len;
            var blk = new byte[shortLen + 1];
            Array.Copy(dat, blk, len);
            Array.Copy(RsRemainder(dat, div), 0, blk, blk.Length - ecc, ecc);
            blocks.Add(blk);
        }
        var outp = new List<byte>();
        for (int i = 0; i < blocks[0].Length; i++)
            for (int j = 0; j < blocks.Count; j++)
                if (i != shortLen - ecc || j >= nShort) outp.Add(blocks[j][i]);
        return outp.ToArray();
    }

    internal static byte[] RsDivisor(int degree)
    {
        var r = new byte[degree];
        r[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < r.Length; j++)
            {
                r[j] = Mul(r[j], root);
                if (j + 1 < r.Length) r[j] ^= r[j + 1];
            }
            root = Mul(root, 0x02);
        }
        return r;
    }

    internal static byte[] RsRemainder(byte[] data, byte[] div)
    {
        var r = new byte[div.Length];
        foreach (var b in data)
        {
            int factor = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1);
            r[^1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= Mul(div[i], factor);
        }
        return r;
    }

    static byte Mul(int x, int y)   // GF(2^8), 다항식 0x11D
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    /// <summary>마스크 벌점 (같은 색 5칸 이상 줄, 2×2 덩어리, 찾기 패턴 닮은꼴, 검정 비율)</summary>
    static int Penalty(bool[,] m, int size)
    {
        int result = 0;
        for (int pass = 0; pass < 2; pass++)   // 0 = 가로줄, 1 = 세로줄
            for (int a = 0; a < size; a++)
            {
                bool color = false;
                int run = 0;
                var h = new int[7];
                for (int b = 0; b < size; b++)
                {
                    bool c = pass == 0 ? m[a, b] : m[b, a];
                    if (c == color)
                    {
                        run++;
                        if (run == 5) result += 3;
                        else if (run > 5) result++;
                    }
                    else
                    {
                        AddHistory(run, h, size);
                        if (!color) result += CountPatterns(h) * 40;
                        color = c;
                        run = 1;
                    }
                }
                if (color) { AddHistory(run, h, size); run = 0; }
                AddHistory(run + size, h, size);
                result += CountPatterns(h) * 40;
            }
        for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
                if (m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) result += 3;
        int dark = 0, total = size * size;
        foreach (bool b in m) if (b) dark++;
        result += ((Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10;
        return result;
    }

    static void AddHistory(int run, int[] h, int size)
    {
        if (h[0] == 0) run += size;   // 줄 시작 앞의 흰 여백
        Array.Copy(h, 0, h, 1, 6);
        h[0] = run;
    }

    static int CountPatterns(int[] h)
    {
        int n = h[1];
        bool core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
        return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
    }
}
