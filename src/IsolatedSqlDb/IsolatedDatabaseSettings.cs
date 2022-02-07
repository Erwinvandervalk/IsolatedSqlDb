using IsolatedSqlDb.Path;

namespace IsolatedSqlDb
{
    public class IsolatedDatabaseSettings
    {
        public string InstanceName { get; private set; }

        public RootedPath Path { get; private set; }
        public string MasterConnectionString { get; private set; }

        public string DatabaseName { get; private set; }

        public int InitialSizeMb = 1;
        public int InitialGrowthMb = 1;

        public IsolatedDatabaseSettings(
            string databaseName,
            RootedPath? path = null, 
            string? masterConnectionString = null, 
            string instanceName = "isolated-db")
        {
            Path = path ?? RootedPath.GetTempPath(databaseName);

            MasterConnectionString = masterConnectionString
                                     ?? $"Data Source=(LocalDb)\\{instanceName};Initial Catalog=master;Integrated Security = SSPI;";

            DatabaseName = databaseName;
            InstanceName = instanceName;
        }

        
    }
}