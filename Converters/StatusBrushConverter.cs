using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BlockUpdateWindowsDefender.Converters
{
    public class StatusBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush SuccessBrush = CreateBrush("#22C55E");
        private static readonly SolidColorBrush WarningBrush = CreateBrush("#F59E0B");
        private static readonly SolidColorBrush DangerBrush = CreateBrush("#EF4444");
        private static readonly SolidColorBrush InfoBrush = CreateBrush("#0EA5E9");
        private static readonly SolidColorBrush MutedBrush = CreateBrush("#94A3B8");
        private static readonly SolidColorBrush DefaultBrush = CreateBrush("#FFFFFF");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string;
            if (string.IsNullOrWhiteSpace(status))
            {
                return DefaultBrush;
            }

            status = status.Trim();

            if (Contains(status, "enabled") || Contains(status, "active") || Contains(status, "running") || Contains(status, "ready") ||
                Contains(status, "bat") || Contains(status, "sẵn sàng") || Contains(status, "san sang") || Contains(status, "hoàn tất") || Contains(status, "hoan tat"))
            {
                return SuccessBrush;
            }

            if (Contains(status, "available"))
            {
                return SuccessBrush;
            }

            if (Contains(status, "disabled") || Contains(status, "stopped") || Contains(status, "failed") || Contains(status, "error") ||
                Contains(status, "tắt") || Contains(status, "tat") || Contains(status, "lỗi") || Contains(status, "loi"))
            {
                return DangerBrush;
            }

            if (Contains(status, "unsupported") || Contains(status, "unavailable") || Contains(status, "không hỗ trợ") || Contains(status, "khong ho tro"))
            {
                return MutedBrush;
            }

            if (Contains(status, "sync") || Contains(status, "refresh") || Contains(status, "detect") ||
                Contains(status, "đồng bộ") || Contains(status, "dong bo") || Contains(status, "làm mới") || Contains(status, "lam moi") || Contains(status, "nhận diện") || Contains(status, "nhan dien") ||
                Contains(status, "đang") ||
                Contains(status, "dang"))
            {
                return InfoBrush;
            }

            if (Contains(status, "warning") || Contains(status, "blocked") || Contains(status, "cảnh báo") || Contains(status, "canh bao") || Contains(status, "bị chặn") || Contains(status, "bi chan"))
            {
                return WarningBrush;
            }

            if (Contains(status, "possible"))
            {
                return InfoBrush;
            }

            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool Contains(string source, string token)
        {
            return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }
}
