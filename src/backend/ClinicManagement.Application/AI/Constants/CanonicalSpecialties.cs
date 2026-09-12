using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ClinicManagement.Application.AI.Constants;

public static class CanonicalSpecialties
{
    public const string Pattern = @"^SP(0[1-9]|1[0-1])$";
    private static readonly Regex CodeRegex = new(Pattern, RegexOptions.Compiled);

    public const string NoiTongQuat = "SP01";
    public const string NhiKhoa = "SP02";
    public const string SanPhuKhoa = "SP03";
    public const string DaLieu = "SP04";
    public const string TaiMuiHong = "SP05";
    public const string TimMach = "SP06";
    public const string CoXuongKhop = "SP07";
    public const string ThanKinh = "SP08";
    public const string NoiTiet = "SP09";
    public const string NhanKhoa = "SP10";
    public const string TieuHoa = "SP11";

    private static readonly Dictionary<string, string> NameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { NoiTongQuat, "Nội tổng quát" },
        { NhiKhoa, "Nhi khoa" },
        { SanPhuKhoa, "Sản phụ khoa" },
        { DaLieu, "Da liễu" },
        { TaiMuiHong, "Tai mũi họng" },
        { TimMach, "Tim mạch" },
        { CoXuongKhop, "Cơ xương khớp" },
        { ThanKinh, "Thần kinh" },
        { NoiTiet, "Nội tiết" },
        { NhanKhoa, "Nhãn khoa" },
        { TieuHoa, "Tiêu hóa" }
    };

    public static readonly IReadOnlyDictionary<string, string> Catalog = new ReadOnlyDictionary<string, string>(NameMap);
    public static readonly IReadOnlyList<string> AllCodes = new ReadOnlyCollection<string>(new List<string>(NameMap.Keys));

    public static bool IsValidFormat(string? code)
    {
        return !string.IsNullOrWhiteSpace(code) && CodeRegex.IsMatch(code.Trim());
    }

    public static bool IsCanonical(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var clean = code.Trim().ToUpperInvariant();
        return IsValidFormat(clean) && NameMap.ContainsKey(clean);
    }

    public static string? GetName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var clean = code.Trim().ToUpperInvariant();
        return NameMap.TryGetValue(clean, out var name) ? name : null;
    }
}
