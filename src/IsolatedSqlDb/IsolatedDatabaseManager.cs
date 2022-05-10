
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using IsolatedSqlDb.Path;
using MartinCostello.SqlLocalDb;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IsolatedSqlDb
{
    /// <summary>
    /// Helps to create isolated SQL Localdb databases for testing purposes. 
    /// </summary>
    public class IsolatedDatabaseManager
    {
        private readonly IsolatedDatabaseSettings _settings;
        private readonly ILogger<IsolatedDatabaseManager> _logger;
        private bool _initialized;

        /// <summary>
        /// Creates a manager. 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        public IsolatedDatabaseManager(
            IsolatedDatabaseSettings settings,
            ILogger<IsolatedDatabaseManager> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// Initialize the system. Starts the sql localdb instance. 
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task Initialize(CancellationToken ct)
        {
            if (_initialized)
                return;

            StartSqlLocalDbInstance();

            _initialized = true;
        }

        private void StartSqlLocalDbInstance()
        {
            var stopwatch = Stopwatch.StartNew();
            var sqlLocalDbProvider = new SqlLocalDbApi();


            const int maxRetryCount = 3;
            var retryCounter = 0;

            ISqlLocalDbInstanceInfo? sqlLocalDbInstance = null;
            do
            {
                try
                {
                    _logger.LogInformation($"Initializing db instance: '{_settings.InstanceName}'");
                    sqlLocalDbInstance = sqlLocalDbProvider.GetOrCreateInstance(_settings.InstanceName);
                }
                catch (InvalidOperationException e) when (e.Message.Contains(_settings.InstanceName))
                {
                    if (retryCounter < maxRetryCount)
                    {
                        retryCounter++;
                        _logger.LogInformation($"Initializing db instance: '{_settings.InstanceName}' collision. " +
                                               $"Attempt: {retryCounter} out of: {maxRetryCount}");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Failed to get db instance: '{_settings.InstanceName}' after: {retryCounter} attempts", e);
                    }
                }
            } while (sqlLocalDbInstance == null);

            sqlLocalDbInstance.Manage().Start();

            stopwatch.Stop();
            _logger.LogInformation($"instance: '{_settings.InstanceName}' initialized. It took: {stopwatch.Elapsed}");
        }

        /// <summary>
        /// Creates an isolated database by copying and attaching the mdb files. 
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<IsolatedDatabase> CreateIsolatedDatabase(CancellationToken ct)
        {
            if (!File.Exists(PreparedMdf)) throw new InvalidOperationException($"MDF '{PreparedMdf}' is not yet prepared. Invoke Prepare()");
            if (!File.Exists(PreparedLdf)) throw new InvalidOperationException($"LDF '{PreparedLdf}' is not yet prepared. Invoke Prepare()");

            const int retry = 3;
            for (int i = 1; i == retry; i++)
            {
                try
                {


                    var databaseName = BuildNewDatabaseName();

                    _logger.LogInformation("Attaching database {database}", databaseName);

                    var targetMdf = _settings.RootedPath.Concat(databaseName + ".mdf");
                    var targetLdf = _settings.RootedPath.Concat(databaseName + "_log.ldf");

                    if (targetMdf.Exists() || targetLdf.Exists())
                    {
                        // File already exists, let's try again. 
                        _logger.LogInformation("File {mdf} or {ldf} already exists. retrying", targetMdf, targetLdf);
                        await Task.Delay(10, ct);
                        continue;
                    }

                    File.Copy(PreparedMdf, targetMdf, true);
                    File.Copy(PreparedLdf, targetLdf, true);

                    await GetMasterDb().ExecuteSql($@"CREATE DATABASE [{databaseName}]
                on (filename = N'{targetMdf}')
                , (filename = N'{targetLdf}')
                FOR ATTACH", ct);

                    _logger.LogInformation("Attached database {database}", databaseName);

                    var db = new IsolatedDatabase(_logger, _settings, BuildConnectionString(databaseName), databaseName);
                    await db.WaitUntilAvailable(ct);
                    return db;
                }
                catch (Exception exception)
                {
                    if (i == retry)
                    {
                        throw;
                    }
                    else
                    {
                        _logger.LogInformation(exception, "Failed to create database. Retrying. ");
                    }
                }
            }

            throw new InvalidOperationException("Failed to create database");

        }

        private IsolatedDatabase GetMasterDb()
        {
            return new IsolatedDatabase(_logger, _settings, _settings.MasterConnectionString);
        }

        private string BuildConnectionString(string databaseName)
        {
            return $"Data Source=(LocalDb)\\{_settings.InstanceName};Initial Catalog={databaseName};Integrated Security = SSPI;";
        }

        /// <summary>
        /// Prepares the isolated databases
        /// </summary>
        /// <param name="prepareDatabase">Action that you can use to create a schema and seed with data. </param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task Prepare(Func<IsolatedDatabase, CancellationToken, Task> prepareDatabase, CancellationToken ct)
        {
            var databaseName = BuildNewDatabaseName();
            await Initialize(ct);

            _settings.RootedPath.EnsureExists();

            string sourceMdf = _settings.RootedPath.Concat(databaseName + ".mdf");
            string sourceLdf = _settings.RootedPath.Concat(databaseName + "_log.ldf");

            var createDatabase = $@"
                CREATE DATABASE [{databaseName}]
                ON PRIMARY (
                    NAME = N'{databaseName}',
                    FILENAME = N'{sourceMdf}',
                    SIZE = {_settings.InitialSizeMb}MB,
                    FILEGROWTH = {_settings.InitialGrowthMb}MB
                    )
                    LOG ON (
                    NAME = N'{databaseName}_log',
                    FILENAME = N'{sourceLdf}'
                    )
                    
";

            var masterDb = GetMasterDb();
            await masterDb.ExecuteSql(createDatabase, ct);


            var isolatedDatabase = new IsolatedDatabase(_logger, _settings, BuildConnectionString(databaseName));
            await isolatedDatabase.WaitUntilAvailable(ct);
            await prepareDatabase(isolatedDatabase, ct);

            _logger.LogDebug("Detaching database {database}", databaseName);
            using (var connection = new SqlConnection(_settings.MasterConnectionString))
            {
                await connection.OpenAsync(ct);
                using (var command = new SqlCommand(
                           $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE",
                           connection))
                {
                    command.CommandType = CommandType.Text;
                    await command.ExecuteNonQueryAsync(ct);
                }

                using (var command = new SqlCommand("master.dbo.sp_detach_db", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@dbname", databaseName);
                    await command.ExecuteNonQueryAsync(ct);
                }
            }

            // Copy the files to the destination
            File.Move(sourceMdf, PreparedLdf, true);
            File.Move(sourceLdf, PreparedMdf, true);

            _logger.LogInformation("Detached database '{database}' and copied files to '{mdf}'",
                databaseName, PreparedMdf);
        }

        private string BuildNewDatabaseName()
        {
            return $"{_settings.SystemName}.{DateTime.Now:yyyyMMddhhmmss}.{DateTime.Now.Ticks}";
        }

        private RootedPath PreparedMdf => _settings.RootedPath.Concat(_settings.SystemName + "_log.ldf");

        private RootedPath PreparedLdf => _settings.RootedPath.Concat(_settings.SystemName + ".mdf");
    }
}