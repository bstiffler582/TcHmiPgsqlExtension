using System;
using System.Text;
using TcHmiSrv.Core;

namespace PgQuery
{
    internal class ValueProtector
    {
        public static string Encode(string value)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static string Decode(string value)
        {
            var base64EncodedBytes = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}
