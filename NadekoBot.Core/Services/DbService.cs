using Microsoft.EntityFrameworkCore;
using NadekoBot.Core.Services.Database;
using System;
using System.IO;
using System.Linq;

namespace NadekoBot.Core.Services
{
    public enum DatabaseTypeEnum
    {
        PostgreSql = 0,
        SqlServer = 1
    }
    public class DbService
    {
        private readonly DbContextOptions<NadekoContext> options;
        private readonly DbContextOptions<NadekoContext> migrateOptions;

        //private static readonly ILoggerFactory _loggerFactory = new LoggerFactory(new[] {
        //    new ConsoleLoggerProvider((category, level)
        //        => category == DbLoggerCategory.Database.Command.Name
        //           && level >= LogLevel.Information, true)
        //    });

        public DbService(IBotCredentials creds)
        {
            var optionsBuilder = new DbContextOptionsBuilder<NadekoContext>()
                //.UseLoggerFactory(_loggerFactory)
                ;
            if (creds.Db.Type.ToLower() == "sqlserver")
            {
                optionsBuilder.UseSqlServer(creds.Db.ConnectionString);
            }
            else
            {
                optionsBuilder.UseNpgsql(creds.Db.ConnectionString);
            }
            options = optionsBuilder.Options;

            //optionsBuilder = new DbContextOptionsBuilder<NadekoContext>();
            //optionsBuilder.UseSqlServer(builder.ToString(), x => x.SuppressForeignKeyEnforcement());
            migrateOptions = optionsBuilder.Options;
        }

        public void Setup()
        {
            using (var context = new NadekoContext(options))
            {
                if (context.Database.GetPendingMigrations().Any())
                {
                    var mContext = new NadekoContext(migrateOptions);
                    mContext.Database.Migrate();
                    mContext.SaveChanges();
                    mContext.Dispose();
                }

                context.EnsureSeedData();
                context.SaveChanges();
            }
        }

        private NadekoContext GetDbContextInternal()
        {
            var context = new NadekoContext(options);
            context.Database.SetCommandTimeout(60);
            var conn = context.Database.GetDbConnection();
            conn.Open();

            return context;
        }

        public IUnitOfWork GetDbContext() => new UnitOfWork(GetDbContextInternal());
        public DatabaseTypeEnum GetDatabaseType()
        {
            using (var uow = GetDbContext())
            {
                if (uow._context.Database.IsNpgsql())
                {
                    return DatabaseTypeEnum.PostgreSql;
                }

                if (uow._context.Database.IsSqlServer())
                {
                    return DatabaseTypeEnum.SqlServer;
                }
            }

            throw new Exception();
        }
    }
}
