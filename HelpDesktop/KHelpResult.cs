using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HelpDesktop
{
    public class KHelpResult
    {
        public bool Success { get; set; }
        public int Code { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Data { get; set; } = "";
    }

    public static class KHelpDeskIntegraNative
    {
        private const string DllName = "KHelpDeskIntegraV3.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int KHelp_Connect(
            string ip,
            int port,
            string privateKey,
            string id,
            string password,
            byte[] result,
            ref int resultSize
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int KHelp_GetLastErrorText(
            byte[] result,
            ref int resultSize
        );

        public static KHelpResult Connect(
            string ip,
            int port,
            string privateKey,
            string id,
            string password
        )
        {
            var buffer = new byte[8192];
            var size = buffer.Length;

            var code = KHelp_Connect(
                ip,
                port,
                privateKey,
                id,
                password,
                buffer,
                ref size
            );

            var data = Encoding.Default
                .GetString(buffer, 0, Math.Max(0, Math.Min(size, buffer.Length)))
                .TrimEnd('\0');

            var result = new KHelpResult
            {
                Code = code,
                Success = code == 0,
                Data = data
            };

            if (!result.Success)
                result.ErrorMessage = GetLastErrorText();

            return result;
        }

        private static string GetLastErrorText()
        {
            var buffer = new byte[4096];
            var size = buffer.Length;

            var code = KHelp_GetLastErrorText(buffer, ref size);

            if (code != 0)
                return "";

            return Encoding.Default
                .GetString(buffer, 0, Math.Max(0, Math.Min(size, buffer.Length)))
                .TrimEnd('\0');
        }
    }
}