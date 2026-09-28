using System.Data;
using Microsoft.Data.SqlClient;

namespace UniflowApi.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("UniflowDB")
            ?? throw new InvalidOperationException("Connection string 'UniflowDB' is not configured.");
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
