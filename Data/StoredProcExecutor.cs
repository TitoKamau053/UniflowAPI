using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using UniflowApi.Common;

namespace UniflowApi.Data;

public class StoredProcExecutor
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<StoredProcExecutor> _logger;

    public StoredProcExecutor(
        IDbConnectionFactory factory,
        ILogger<StoredProcExecutor> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string procName,
        object? parameters = null)
    {
        using var conn = _factory.CreateConnection();

        try
        {
            return await conn.QueryAsync<T>(
                procName,
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex) when (IsRegisteredDbError(ex))
        {
            throw ToApiException(ex);
        }
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string procName,
        object? parameters = null)
    {
        using var conn = _factory.CreateConnection();

        try
        {
            return await conn.QuerySingleOrDefaultAsync<T>(
                procName,
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex) when (IsRegisteredDbError(ex))
        {
            throw ToApiException(ex);
        }
    }
    
    public async Task<(TFirst? First, List<TSecond> Second)> QueryMultipleAsync<TFirst, TSecond>(
        string procName,
        object? parameters = null)
    {
        using var conn = _factory.CreateConnection();

        try
        {
            await using var multi = await conn.QueryMultipleAsync(
                procName,
                parameters,
                commandType: CommandType.StoredProcedure);

            var first = await multi.ReadSingleOrDefaultAsync<TFirst>();

            var second = (await multi.ReadAsync<TSecond>())
                .ToList();

            return (first, second);
        }
        catch (SqlException ex) when (IsRegisteredDbError(ex))
        {
            throw ToApiException(ex);
        }
    }

    public async Task<DynamicParameters> ExecuteWithOutputAsync(
        string procName,
        DynamicParameters parameters)
    {
        using var conn = _factory.CreateConnection();

        try
        {
            await conn.ExecuteAsync(
                procName,
                parameters,
                commandType: CommandType.StoredProcedure);

            return parameters;
        }
        catch (SqlException ex) when (IsRegisteredDbError(ex))
        {
            throw ToApiException(ex);
        }
    }

    public async Task ExecuteAsync(
        string procName,
        object? parameters = null)
    {
        using var conn = _factory.CreateConnection();

        try
        {
            await conn.ExecuteAsync(
                procName,
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex) when (IsRegisteredDbError(ex))
        {
            throw ToApiException(ex);
        }
    }

    private bool IsRegisteredDbError(SqlException ex)
    {
        if (ex.Number >= 50000)
            return true;

        _logger.LogError(
            ex,
            "Unexpected SQL error {Number} calling a stored procedure",
            ex.Number);

        return false;
    }

    private static int HttpStatusForCode(int errorCode) => errorCode switch
    {
        >= 50000 and < 51000 => 400,
        >= 51000 and < 52000 => 404,
        >= 52000 and < 53000 => 401,
        >= 53000 and < 54000 => 403,
        >= 54000 and < 55000 => 409,
        _ => 400
    };

    private static ApiException ToApiException(SqlException ex) =>
        new(
            ex.Message,
            HttpStatusForCode(ex.Number),
            errors: null,
            dbErrorCode: ex.Number);
}