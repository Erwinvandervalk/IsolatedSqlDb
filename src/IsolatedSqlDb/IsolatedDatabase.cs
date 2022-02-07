using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IsolatedSqlDb
{
    public class IsolatedDatabase : IAsyncDisposable, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IsolatedDatabaseSettings _settings;
        private readonly string? _databaseName;

        private static readonly Regex _splitCommandsOnGoRegex = new Regex(@"^(\s|\t)*go(\s\t)?.*",
            RegexOptions.Multiline
            | RegexOptions.IgnoreCase
            | RegexOptions.Compiled);
        public string ConnectionString { get; }

        public IsolatedDatabase(ILogger logger, IsolatedDatabaseSettings settings, string connectionString,
            string databaseName) : this(logger, settings, connectionString)
        {
            _databaseName = databaseName;
        }

        internal IsolatedDatabase(ILogger logger, IsolatedDatabaseSettings settings,  string connectionString)
        {
            _logger = logger;
            _settings = settings;
            ConnectionString = connectionString;
        }


        public async Task ExecuteSql(string command, CancellationToken ct)
        {
            using (var connection = await OpenConnection(ct))
            {
                connection.InfoMessage += (_, args) =>
                    {
                        _logger.LogDebug("db ==> msg: {msg} from {source}, errors: {errors}", 
                            args.Message, 
                            args.Source, args.Errors);
                    };

                foreach (var s in _splitCommandsOnGoRegex.Split(command))
                {
                    string statement = s.Trim();
                    if (string.IsNullOrEmpty(statement)) continue;

                    using (var cmd = new SqlCommand(statement, connection))
                    {
                        _logger.LogDebug("db ==> execute script {script}", statement);
                        cmd.CommandType = CommandType.Text;

                        await cmd.ExecuteNonQueryAsync(ct);
                        _logger.LogDebug("db ==> finished script {script}", statement);

                    }
                }
            }

        }

        public async Task WaitUntilAvailable(CancellationToken ct)
        {
            for (var i = 0; i <= 3; i++)
                try
                {
                    using (var c = new SqlConnection(ConnectionString))
                    {
                        await c.OpenAsync(ct);
                    }
                }
                catch (Exception e)
                {
                    if (i < 3)
                    {
                        _logger.LogWarning("Error connecting to database {connectionString}. Retrying...", ConnectionString);
                    }
                    else
                    {
                        _logger.LogError(e, "Error connecting to database {connectionString}",ConnectionString);
                        throw;
                    }
                }
        }

        private async Task<SqlConnection> OpenConnection(CancellationToken ct)
        {
            var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        public async Task DropDatabase(CancellationToken ct)
        {
            if (_databaseName == null) throw new InvalidOperationException("Cannot drop database when name is null");

            _logger.LogInformation("Db -> Deleting database {dbName}", _databaseName);
            await PerformWithRetry(3, ct, async (ct1) =>
                {
                    var master = new IsolatedDatabase(_logger, _settings, _settings.MasterConnectionString);

                    await master.ExecuteSql(@$"
IF EXISTS (SELECT name FROM master.dbo.sysdatabases WHERE name = '{_databaseName}')
BEGIN
    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    EXEC master.dbo.sp_detach_db @dbname = N'{_databaseName}';
END", ct1);
                });

            
            await PerformWithRetry(3, ct, async (_) => File.Delete(_settings.Path.Concat(_databaseName + ".mdf")));
            await PerformWithRetry(3, ct, async (_) => File.Delete(_settings.Path.Concat(_databaseName + "_log.ldf")));
        }

        private async Task PerformWithRetry(int times, CancellationToken ct, Func<CancellationToken, Task> a)
        {
            for (int i = 0; i <= times; i++)
            {
                try
                {
                    await a(ct);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                    if (i == times)
                        throw;
                }
            }
        }

        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            await Task.Run(async () =>
                {
                    await DropDatabase(CancellationToken.None);
                });
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Task.Run(async () =>
                {
                    await DropDatabase(CancellationToken.None);
                });
        }
    }
}