using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IsolatedSqlDb.Path;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace IsolatedSqlDb.Tests;

public class IsolatedDbManagerTests
{
    private readonly SerilogLoggerFactory _loggerFactory;
    private IsolatedDatabaseManager _mgr;
    private IsolatedDatabaseSettings _isolatedDatabaseSettings;
    private string _dbName = "test" + "-" + Guid.NewGuid();

    public IsolatedDbManagerTests(ITestOutputHelper output)
    {
        var logger = new LoggerConfiguration()
            .WriteTo.TestOutput(output)
            .CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(logger);
        _isolatedDatabaseSettings = new IsolatedDatabaseSettings(_dbName);
        _mgr = new IsolatedDatabaseManager(_isolatedDatabaseSettings, _loggerFactory.CreateLogger<IsolatedDatabaseManager>());
    }

    [Fact]
    public async Task When_preparing_db_then_database_then_db_files_are_created()
    {
        // Prepare database
        await _mgr.Prepare(UseEfToCreateDbSchema, CancellationToken.None);

        var files = Directory.GetFiles(_isolatedDatabaseSettings.Path);

        files.Any(x => x.EndsWith(_dbName + ".mdf")).ShouldBe(true);
        files.Any(x => x.EndsWith(_dbName + "_log.ldf")).ShouldBeTrue();
    }

    [Fact]
    public async Task When_disposing_db_then_db_files_are_removed()
    {
        // Prepare database
        await _mgr.Prepare(UseEfToCreateDbSchema, CancellationToken.None);

        Directory.GetFiles(_isolatedDatabaseSettings.Path).Length.ShouldBe(2, "2 for the 'prepared'");

        var db = await _mgr.CreateIsolatedDatabase(CancellationToken.None);

        Directory.GetFiles(_isolatedDatabaseSettings.Path).Length.ShouldBe(4, "2 for the 'prepared' and 2 for the actual db");
        await db.DisposeAsync();

        Directory.GetFiles(_isolatedDatabaseSettings.Path).Length.ShouldBe(2, "2 for the 'prepared'");
    }


    [Fact]
    public async Task Can_create_database_from_prepared_database()
    {
        // Prepare database
        await _mgr.Prepare(UseEfToCreateDbSchema, CancellationToken.None);

        // Create new database from prepared. 
        using var db = await _mgr.CreateIsolatedDatabase(CancellationToken.None);

        // Create dbcontext to verify if it works. 
        var dbContext = CreateTestDbContext(db);
        await dbContext.Tests.AddAsync(new TestTable()
        {
            Id = Guid.NewGuid(),
            Name = "test"
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task UseEfToCreateDbSchema(IsolatedDatabase isolatedDatabase, CancellationToken cancellationToken)
    {
        var context = CreateTestDbContext(isolatedDatabase);

        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static TestDbContext CreateTestDbContext(IsolatedDatabase isolatedDatabase)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(isolatedDatabase.ConnectionString).Options;

        var context = new TestDbContext(options);
        return context;
    }

    public class TestTable
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<TestTable> Tests { get; set; }
    }


}