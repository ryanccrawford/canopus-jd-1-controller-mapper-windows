using System.Globalization;
using HidSharp;

namespace CanopusMapApp
{
    public sealed class HidDeviceSnapshot
    {
        public string DevicePath { get; init; } = "n/a";
        public string ProductName { get; init; } = "n/a";
        public string Manufacturer { get; init; } = "n/a";
        public string SerialNumber { get; init; } = "n/a";
        public int VendorId { get; init; }
        public int ProductId { get; init; }
        public int ReleaseNumberBcd { get; init; }
        public int MaxInputReportLength { get; init; }
        public int MaxOutputReportLength { get; init; }
        public int MaxFeatureReportLength { get; init; }
    }

    public static class HidDiagnostics
    {
        public static HidDeviceSnapshot CreateSnapshot(HidDevice device)
        {
            string GetSafeString(Func<string> getter)
            {
                try { return getter() ?? "n/a"; } catch { return "n/a"; }
            }
            int GetSafeInt(Func<int> getter)
            {
                try { return getter(); } catch { return 0; }
            }

            return new HidDeviceSnapshot
            {
                DevicePath = device.DevicePath ?? "n/a",
                ProductName = GetSafeString(() => device.GetProductName()),
                Manufacturer = GetSafeString(() => device.GetManufacturer()),
                SerialNumber = GetSafeString(() => device.GetSerialNumber()),
                VendorId = device.VendorID,
                ProductId = device.ProductID,
                ReleaseNumberBcd = device.ReleaseNumberBcd,
                MaxInputReportLength = GetSafeInt(() => device.GetMaxInputReportLength()),
                MaxOutputReportLength = GetSafeInt(() => device.GetMaxOutputReportLength()),
                MaxFeatureReportLength = GetSafeInt(() => device.GetMaxFeatureReportLength())
            };
        }

        public static bool TryParseHexBytes(string text, out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Enter one or more hex bytes.";
                return false;
            }

            var tokens = text
                .Split(new[] { ' ', '\t', '\r', '\n', ',', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token)
                .ToArray();

            var parsed = new List<byte>(tokens.Length);
            foreach (var token in tokens)
            {
                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                {
                    error = $"Invalid hex byte: {token}";
                    return false;
                }

                parsed.Add(value);
            }

            bytes = parsed.ToArray();
            return true;
        }

        public static string FormatBytes(byte[] data)
        {
            return string.Join(" ", data.Select(static b => b.ToString("X2")));
        }
    }
}
