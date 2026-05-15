using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

SQLitePCL.Batteries_V2.Init();

const string dbPath = @"C:\ProgramData\RunBook\Desktop\TenMfg_data\_core\runbook.db";
const string shopId = "c0c22250-54ee-420b-8d3c-50dbe8907c89";

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = """
    SELECT Id, EmployeeCode, COALESCE(NULLIF(PreferredName, ''), FullName) AS DisplayName, MobilePinSaltBase64, MobilePinHashBase64
    FROM HrEmployees
    WHERE ShopId = $shopId
      AND Status = 'Active'
      AND WorkstationAccessEnabled = 1
      AND length(COALESCE(MobilePinSaltBase64, '')) > 0
      AND length(COALESCE(MobilePinHashBase64, '')) > 0
    ORDER BY Id;
    """;
cmd.Parameters.AddWithValue("$shopId", shopId);

using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    var id = reader.GetInt32(0);
    var employeeCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
    var displayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
    var saltBase64 = reader.GetString(3);
    var hashBase64 = reader.GetString(4);
    var passcode = RecoverPasscode(saltBase64, hashBase64);
    Console.WriteLine($"{id}|{employeeCode}|{displayName}|{passcode}");
}

static string RecoverPasscode(string saltBase64, string expectedHashBase64)
{
    var salt = Convert.FromBase64String(saltBase64);
    using var sha = SHA256.Create();

    for (var length = 4; length <= 6; length++)
    {
        var max = (int)Math.Pow(10, length);
        for (var value = 0; value < max; value++)
        {
            var pin = value.ToString(new string('0', length), CultureInfo.InvariantCulture);
            var bytes = Encoding.UTF8.GetBytes(pin);
            var combined = new byte[salt.Length + bytes.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(bytes, 0, combined, salt.Length, bytes.Length);
            var hash = Convert.ToBase64String(sha.ComputeHash(combined));
            if (string.Equals(hash, expectedHashBase64, StringComparison.Ordinal))
                return pin;
        }
    }

    return "<not-found>";
}
