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
    /// <summary>
    /// An instance of a sql database that's isolated for testing purposes.
    /// Disposing of this instance will delete it. 
    /// </summary>
    public class IsolatedDatabase : IAsyncDisposable, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IsolatedDatabaseSettings _settings;
        private readonly string? _databaseName;

        private static readonly Regex _splitCommandsOnGoRegex = new Regex(@"^(\s|\t)*go(\s\t)?.*",
            RegexOptions.Multiline
            | RegexOptions.IgnoreCase
            | RegexOptions.Compiled);

        /// <summary>
        /// The connection string for this database instance. 
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Creates an instance. 
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="settings"></param>
        /// <param name="connectionString"></param>
        /// <param name="databaseName"></param>
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


        /// <summary>
        /// Allows you to execute sql blocks. 
        /// </summary>
        /// <param name="command"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Attempts to make a connection to the db. In rare occurances, it might take a while
        /// to become available. 
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
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

        private bool _dropped;

        /// <summary>
        /// Will drop this database. 
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task DropDatabase(CancellationToken ct)
        {
            if (_dropped) return;

            _dropped = true;

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

            
            await PerformWithRetry(3, ct, async (_) => File.Delete(_settings.RootedPath.Concat(_databaseName + ".mdf")));
            await PerformWithRetry(3, ct, async (_) => File.Delete(_settings.RootedPath.Concat(_databaseName + "_log.ldf")));
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

        /// <summary>
        /// Disposes and deletes the database. This waits until the db is actually deleted. 
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Deletes the database. does not wait for the db actually to be deleted. 
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Run the 'dropping' on a different thread. It's not awaited so it may not complete. 
            // The reason for this is that dropping it takes time. Dont' want to wait for this.
            // Disk space is cheap (if you clean up regularly), time is not. 

            Task.Run(async () =>
                {
                    await DropDatabase(CancellationToken.None);
                });
        }
    }
}