using H1Stats.Core.Models;

namespace H1Stats.Core.Services;

/// <summary>
/// Maps CM2 study-requested codes (<c>TEST.TEST_REQUIRED</c>) and ward to Dr Summary OOS rows.
/// Occasion-of-service titles (TTX, SE, DSE, IOE, TEE, DCCS, consults) are the display grain.
/// </summary>
public static class Cm2DrSummaryMapping
{
    public const int TransthoracicEcho = 1;
    public const int ExerciseStressEcho = 2;
    public const int DobutamineStressEcho = 3;
    public const int TransoesophagealEcho = 4;

    /// <summary>Theatre 8 and Theatre Admissions — Intra-operative TEE (IOE).</summary>
    public static readonly HashSet<int> IoeWardCodes = [20, 24];

    /// <summary>
    /// Cardioversion diary services: DCCS only (6) and TEE/DCCS (101).
    /// </summary>
    public static readonly IReadOnlyList<int> DccsOccServiceIds = [6, 101];

    /// <summary>Sentinel column id for all Hearts 1st reporting cardiologists combined.</summary>
    public const string AllDoctorsColumnId = "all-drs-total";

    /// <summary>Cardiologists omitted from Dr Summary columns (RHS, Dr Masashi Ura).</summary>
    public static readonly HashSet<string> ExcludedCardiologistIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "3581",
        "1761",
    };

    public static bool IsExcludedCardiologist(string? raw)
    {
        var id = Cm2CardiologistMapping.NormalizeId(raw);
        return !string.IsNullOrWhiteSpace(id) && ExcludedCardiologistIds.Contains(id);
    }

    public static bool IsIoeWard(string? ward)
    {
        if (!TryParseWard(ward, out var code))
            return false;
        return IoeWardCodes.Contains(code);
    }

    public static bool TryParseWard(string? ward, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(ward))
            return false;
        if (!decimal.TryParse(ward.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return false;
        code = (int)n;
        return true;
    }
}

/// <summary>Per-column OOS counters for Dr Summary.</summary>
public sealed class DrSummaryAccumulator
{
    private readonly int _n;
    private readonly int[] _echoOp;
    private readonly int[] _echoIp;
    private readonly int[] _echoTotal;
    private readonly int[] _dseOp;
    private readonly int[] _dseIp;
    private readonly int[] _dseTotal;
    private readonly int[] _seOp;
    private readonly int[] _seIp;
    private readonly int[] _seTotal;
    private readonly int[] _ioe;
    private readonly int[] _tee;
    private readonly int[] _teeTotal;
    private readonly int[] _dccs;
    private readonly int[] _consults;

    public DrSummaryAccumulator(int columnCount)
    {
        _n = columnCount;
        _echoOp = new int[_n];
        _echoIp = new int[_n];
        _echoTotal = new int[_n];
        _dseOp = new int[_n];
        _dseIp = new int[_n];
        _dseTotal = new int[_n];
        _seOp = new int[_n];
        _seIp = new int[_n];
        _seTotal = new int[_n];
        _ioe = new int[_n];
        _tee = new int[_n];
        _teeTotal = new int[_n];
        _dccs = new int[_n];
        _consults = new int[_n];
    }

    public int UnassignedDccs { get; private set; }

    public void AddTest(int col, string? typeCode, string? ward, int count)
    {
        if (col < 0 || col >= _n)
            return;
        if (!decimal.TryParse(typeCode, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var typeDec))
            return;
        var typeNum = (int)typeDec;
        var outpatient = WardClassification.IsCm2Outpatient(ward);
        var inpatient = WardClassification.IsCm2Inpatient(ward);

        switch (typeNum)
        {
            case Cm2DrSummaryMapping.TransthoracicEcho:
                Add(_echoTotal, col, count);
                if (outpatient) Add(_echoOp, col, count);
                else if (inpatient) Add(_echoIp, col, count);
                break;
            case Cm2DrSummaryMapping.DobutamineStressEcho:
                Add(_dseTotal, col, count);
                if (outpatient) Add(_dseOp, col, count);
                else if (inpatient) Add(_dseIp, col, count);
                break;
            case Cm2DrSummaryMapping.ExerciseStressEcho:
                Add(_seTotal, col, count);
                if (outpatient) Add(_seOp, col, count);
                else if (inpatient) Add(_seIp, col, count);
                break;
            case Cm2DrSummaryMapping.TransoesophagealEcho:
                Add(_teeTotal, col, count);
                if (Cm2DrSummaryMapping.IsIoeWard(ward))
                    Add(_ioe, col, count);
                else
                    Add(_tee, col, count);
                break;
        }
    }

    public void AddConsult(int col, int count)
    {
        if (col >= 0 && col < _n)
            _consults[col] += count;
    }

    public void AddDccs(int col)
    {
        if (col >= 0 && col < _n)
            _dccs[col] += 1;
        else
            UnassignedDccs += 1;
    }

    public IReadOnlyList<DrSummaryRow> ToRows()
    {
        var imaging = new int[_n];
        for (var i = 0; i < _n; i++)
            imaging[i] = _echoTotal[i] + _dseTotal[i] + _seTotal[i] + _teeTotal[i];

        DrSummaryRow Row(string key, string label, string kind, int[] counts) =>
            new(key, label, kind, counts.ToArray());

        return
        [
            Row("echo", "Echo", "item", _echoOp),
            Row("echo-inpt", "Echo - InPt", "item", _echoIp),
            Row("echo-total", "Echo - Total", "total", _echoTotal),
            Row("dse", "DSE", "item", _dseOp),
            Row("dse-inpt", "DSE - InPt", "item", _dseIp),
            Row("dse-total", "DSE - Total", "total", _dseTotal),
            Row("se", "SE", "item", _seOp),
            Row("se-inpt", "SE - InPt", "item", _seIp),
            Row("se-total", "SE - Total", "total", _seTotal),
            Row("ioe", "IOE", "item", _ioe),
            Row("tee", "TEE", "item", _tee),
            Row("tee-total", "TEE - Total", "total", _teeTotal),
            Row("imaging", "", "subtotal", imaging),
            Row("spacer-dccs", "", "spacer", new int[_n]),
            Row("dccs", "DCCS", "item", _dccs),
            Row("spacer-consults", "", "spacer", new int[_n]),
            Row("consults", "Consults", "item", _consults),
        ];
    }

    private static void Add(int[] target, int col, int count) => target[col] += count;
}
