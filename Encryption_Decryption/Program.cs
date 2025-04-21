using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.RegularExpressions;

public class Program
{
    private static readonly string connectionString =
      "Server=(localdb)\\MSSQLLocalDB;Database=CDP;Trusted_Connection=True";
    private static readonly List<string> ConfidentialEntries = new() { "ActivityName" };

    public static void Main() => MainProgram();

    private static void MainProgram()
    {
        Console.WriteLine("請輸入功能資料庫名稱：");
        var dbName = Console.ReadLine()?.Trim() ?? "";
        var tableNames = GetTableNames(dbName);

        while (true)
        {
            Console.WriteLine("1.存取資料庫原有設計");
            Console.WriteLine("2.資料庫隱碼");
            Console.WriteLine("3.資料庫解碼");
            Console.WriteLine("請輸入功能(數字) ：");

            var selectVal = Console.ReadLine()?.Trim();

            using var conn = new SqlConnection(connectionString);
            conn.Open();

            switch (selectVal)
            {
                case "1":
                    ScanTableSchema(conn, tableNames);
                    return;
                case "2":
                    ProcessConfidentialData(conn, dbName, tableNames, encode: true);
                    return;
                case "3":
                    ProcessConfidentialData(conn, dbName, tableNames, encode: false);
                    return;
                default:
                    Console.WriteLine("請輸入正確選項（1~3）");
                    break;
            }
        }
    }

    private static void ProcessConfidentialData(SqlConnection conn, string dbName, List<string> tableNames, bool encode)
    {
        foreach (var tableName in tableNames)
        {
            if (!IsTableAlreadyScanned(conn, tableName))
            {
                Console.WriteLine($"不存在：{tableName}，跳過。");
                continue;
            }

            var tableColumns = GetTableColumns(conn, dbName, tableName);

            foreach (var column in tableColumns)
            {
                if (!ConfidentialEntries.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"不隱碼欄位：{tableName}.{column}");
                    continue;
                }

                Console.WriteLine($"{tableName}.{column} => ******");

                var originalType = encode ? "NVARCHAR(MAX)" : GetFullType(conn, tableName, column) ?? "NVARCHAR(MAX)";
                UpdateColumnType(conn, tableName, column, originalType);

                var columnValues = GetColumnValues(conn, tableName, column);

                foreach (var value in columnValues)
                {
                    Console.WriteLine($"{tableName}.{column}.{value} => ******");

                    if (encode && !IsBase64String(value))
                    {
                        var encoded = Encode(value);
                        InsertColumnData(conn, tableName, column, encoded, value);
                        Console.WriteLine($"{tableName}.{column} => {encoded}");
                    }
                    else if (!encode && IsBase64String(value))
                    {
                        var decoded = Decode(value);
                        InsertColumnData(conn, tableName, column, decoded, value);
                        Console.WriteLine($"{tableName}.{column} => {decoded}");
                    }
                    else
                    {
                        Console.WriteLine("符合條件 => 跳過");
                    }
                }
            }
        }
    }

    private static List<string> GetTableNames(string dbName)
    {
        var tableNames = new List<string>();
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var command = new SqlCommand($"USE [{dbName}]; SELECT name FROM sys.tables", conn);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            tableNames.Add(reader.GetString(0));
        return tableNames;
    }

    private static List<string> GetTableColumns(SqlConnection conn, string dbName, string tableName)
    {
        var columns = new List<string>();
        using var command = new SqlCommand($"USE [{dbName}]; SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName", conn);
        command.Parameters.AddWithValue("@TableName", tableName);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(0));
        return columns;
    }

    private static List<string> GetColumnValues(SqlConnection conn, string tableName, string column)
    {
        var values = new List<string>();
        using var command = new SqlCommand($"SELECT DISTINCT [{column}] FROM [{tableName}] WHERE [{column}] IS NOT NULL", conn);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    private static bool IsTableAlreadyScanned(SqlConnection conn, string tableName)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM TableSchemaInfo WHERE TableName = @TableName", conn);
        command.Parameters.AddWithValue("@TableName", tableName);
        return (int)command.ExecuteScalar() > 0;
    }

    private static string? GetFullType(SqlConnection conn, string tableName, string columnName)
    {
        using var command = new SqlCommand("SELECT FullType FROM TableSchemaInfo WHERE TableName = @TableName AND ColumnName = @ColumnName", conn);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        return command.ExecuteScalar() as string;
    }

    private static void UpdateColumnType(SqlConnection conn, string tableName, string columnName, string newType)
    {
        if (!IsValidSqlIdentifier(tableName) || !IsValidSqlIdentifier(columnName)) return;
        using var command = new SqlCommand($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {newType}", conn);
        command.ExecuteNonQuery();
    }

    private static void InsertColumnData(SqlConnection conn, string tableName, string columnName, string newValue, string oldValue)
    {
        using var command = new SqlCommand($"UPDATE [{tableName}] SET [{columnName}] = @NewValue WHERE [{columnName}] = @OldValue", conn);
        command.Parameters.AddWithValue("@NewValue", newValue);
        command.Parameters.AddWithValue("@OldValue", oldValue);
        command.ExecuteNonQuery();
    }

    private static void ScanTableSchema(SqlConnection conn, List<string> tableNames)
    {
        foreach (var tableName in tableNames)
        {
            using var columnCommand = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName", conn);
            columnCommand.Parameters.AddWithValue("@TableName", tableName);
            using var reader = columnCommand.ExecuteReader();
            var columnData = new List<(string Column, string Type)>();
            while (reader.Read())
                columnData.Add((reader.GetString(0), reader.GetString(1).ToUpper()));

            reader.Close(); // Close before next command

            foreach (var (column, type) in columnData)
            {
                using var insertCommand = new SqlCommand("INSERT INTO TableSchemaInfo (TableName, ColumnName, FullType) VALUES (@TableName, @ColumnName, @FullType)", conn);
                insertCommand.Parameters.AddWithValue("@TableName", tableName);
                insertCommand.Parameters.AddWithValue("@ColumnName", column);
                insertCommand.Parameters.AddWithValue("@FullType", type);
                insertCommand.ExecuteNonQuery();
            }
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static bool IsBase64String(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        return (s.Length % 4 == 0) && Regex.IsMatch(s, "^[A-Za-z0-9+/]*={0,2}$", RegexOptions.None);
    }

    private static bool IsValidSqlIdentifier(string name) => Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
}
