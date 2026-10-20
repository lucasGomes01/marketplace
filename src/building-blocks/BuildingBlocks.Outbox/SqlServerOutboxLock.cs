using System.Data;
using Microsoft.Data.SqlClient;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Lock do SQL Server (<c>sp_getapplock</c>) preso a uma conexão dedicada: vale enquanto a conexão estiver aberta.
/// Se a instância cair, a conexão fecha, o lock é liberado e outra cópia do serviço assume o relay.
/// </summary>
public sealed class SqlServerOutboxLock(string connectionString, string resource) : IOutboxLock
{
    // Sem pool: com pool, fechar a conexão não encerra a sessão e o lock ficaria preso a uma conexão ociosa.
    private readonly string _connectionString = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    public async Task<IOutboxLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", resource);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 0);
            var result = command.Parameters.Add("@Result", SqlDbType.Int);
            result.Direction = ParameterDirection.ReturnValue;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // 0 = concedido, 1 = concedido depois de esperar; negativo = outra sessão tem o lock.
            if ((int)result.Value >= 0)
            {
                return new Lease(connection, resource);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Lease(SqlConnection connection, string resource) : IOutboxLease
    {
        public async Task<bool> IsHeldAsync(CancellationToken cancellationToken)
        {
            if (connection.State != ConnectionState.Open)
            {
                return false;
            }
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Session')";
                command.Parameters.AddWithValue("@resource", resource);
                return (string?)await command.ExecuteScalarAsync(cancellationToken) == "Exclusive";
            }
            catch (SqlException)
            {
                return false;
            }
        }

        // Fechar a conexão libera o lock da sessão.
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
