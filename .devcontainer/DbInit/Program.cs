using System;
using System.IO;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
            ?? "Server=db;Database=master;User Id=sa;Password=YourStrong!Password;TrustServerCertificate=True;";
        
        Console.WriteLine("Connecting to SQL Server...");
        for (int i = 0; i < 30; i++)
        {
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                Console.WriteLine("Connected successfully!");
                break;
            }
            catch
            {
                Console.WriteLine("Waiting for SQL Server to be ready...");
                System.Threading.Thread.Sleep(2000);
            }
        }
        
        using (var conn = new SqlConnection(connStr))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MPMS') CREATE DATABASE MPMS;";
                cmd.ExecuteNonQuery();
                Console.WriteLine("Database MPMS verified/created.");
            }
        }
        
        var mpmsConnStr = connStr.Replace("Database=master", "Database=MPMS");
        using (var conn = new SqlConnection(mpmsConnStr))
        {
            conn.Open();
            ExecuteSqlFile(conn, "/workspace/DbSchema.sql");
            ExecuteSqlFile(conn, "/workspace/UpgradeSchemaV05.sql");
            ExecuteSqlFile(conn, "/workspace/UpgradeSchemaV06.sql");
        }
        Console.WriteLine("Database initialization complete!");
    }
    
    static void ExecuteSqlFile(SqlConnection conn, string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Warning: File {path} not found.");
            return;
        }
        Console.WriteLine($"Executing {path}...");
        var script = File.ReadAllText(path);
        
        // Split by GO commands on separate lines (case-insensitive, optional whitespace)
        var statements = System.Text.RegularExpressions.Regex.Split(
            script, 
            @"^\s*GO\s*$", 
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        foreach (var stmt in statements)
        {
            var trimmed = stmt.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = trimmed;
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing statement: {ex.Message}");
                    Console.WriteLine($"Statement: {trimmed.Substring(0, Math.Min(100, trimmed.Length))}...");
                    throw;
                }
            }
        }
    }
}
