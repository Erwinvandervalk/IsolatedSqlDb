using IsolatedSqlDb.Path;

namespace IsolatedSqlDb
{
    public class IsolatedDatabaseSettings
    {
        /// <summary>
        /// The name of the sql local db instance used. 
        /// </summary>
        public string InstanceName { get; private set; }

        /// <summary>
        /// The path where the sql files will be stored. By default, this is the temp folder
        /// </summary>

        public string Path { get; private set; }

        /// <summary>
        /// The path as rooted path
        /// </summary>
        internal RootedPath RootedPath => new RootedPath(Path);

        /// <summary>
        /// Connection string to master db of the instance. By default, this points to the instance provided. 
        /// </summary>
        public string MasterConnectionString { get; private set; }

        /// <summary>
        /// The name of the system that's currently under test. This is used to create unique database names,
        /// file names, etc
        /// </summary>
        public string SystemName { get; private set; }

        /// <summary>
        /// How big should the db be when creating
        /// </summary>
        public int InitialSizeMb = 1;

        /// <summary>
        /// How fast should the db grow?
        /// </summary>
        public int InitialGrowthMb = 1;

        /// <summary>
        /// Creates IsolatedDatabaseSettings 
        /// </summary>
        /// <param name="systemName">The name of the system that's currently under test. This is used to create unique database names,
        /// file names, etc</param>
        /// <param name="path">The path where the sql files will be stored. By default, this is the temp folder</param>
        /// <param name="masterConnectionString">Connection string to master db of the instance. By default, this points to the instance provided. </param>
        /// <param name="instanceName">The name of the sql local db instance used. </param>
        public IsolatedDatabaseSettings(
            string systemName,
            string? path = null, 
            string? masterConnectionString = null, 
            string instanceName = "isolated-db")
        {
            Path = path ?? RootedPath.GetTempPath(systemName);

            MasterConnectionString = masterConnectionString
                                     ?? $"Data Source=(LocalDb)\\{instanceName};Initial Catalog=master;Integrated Security = SSPI;";

            SystemName = systemName;
            InstanceName = instanceName;
        }

        
    }
}