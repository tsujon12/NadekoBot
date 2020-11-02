using Microsoft.EntityFrameworkCore;
using NadekoBot.Core.Services.Database.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NadekoBot.Core.Services.Database.Repositories.Impl
{
    public class WaifuRepository : Repository<WaifuInfo>, IWaifuRepository
    {
        public WaifuRepository(DbContext context) : base(context)
        {
        }

        public WaifuInfo ByWaifuUserId(ulong userId, Func<DbSet<WaifuInfo>, IQueryable<WaifuInfo>> includes = null)
        {
            if (includes == null)
            {
                return _set.Include(wi => wi.Waifu)
                            .Include(wi => wi.Affinity)
                            .Include(wi => wi.Claimer)
                            .Include(wi => wi.Items)
                            .FirstOrDefault(wi => wi.WaifuId == _context.Set<DiscordUser>()
                                .AsQueryable()
                                .Where(x => x.UserId == userId)
                                .Select(x => x.Id)
                                .FirstOrDefault());
            }

            return includes(_set)
                .AsQueryable()
                .FirstOrDefault(wi => wi.WaifuId == _context.Set<DiscordUser>()
                    .AsQueryable()
                    .Where(x => x.UserId == userId)
                    .Select(x => x.Id)
                    .FirstOrDefault());
        }

        public IEnumerable<string> GetWaifuNames(ulong userId)
        {
            var waifus = _set.AsQueryable().Where(x => x.ClaimerId != null &&
                x.ClaimerId == _context.Set<DiscordUser>()
                    .AsQueryable()
                    .Where(y => y.UserId == userId)
                    .Select(y => y.Id)
                    .FirstOrDefault())
                .Select(x => x.WaifuId);

            return _context.Set<DiscordUser>()
                .AsQueryable()
                .Where(x => waifus.Contains(x.Id))
                .Select(x => x.Username + "#" + x.Discriminator)
                .ToList();

        }

        public IEnumerable<WaifuLbResult> GetTop(int count, int skip = 0)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return new List<WaifuLbResult>();

            return _set.Include(wi => wi.Waifu)
                        .Include(wi => wi.Affinity)
                        .Include(wi => wi.Claimer)
                    .OrderByDescending(wi => wi.Price)
                    .Skip(skip)
                    .Take(count)
                    .Select(x => new WaifuLbResult
                    {
                        Affinity = x.Affinity == null ? null : x.Affinity.Username,
                        AffinityDiscrim = x.Affinity == null ? null : x.Affinity.Discriminator,
                        Claimer = x.Claimer == null ? null : x.Claimer.Username,
                        ClaimerDiscrim = x.Claimer == null ? null : x.Claimer.Discriminator,
                        Username = x.Waifu.Username,
                        Discrim = x.Waifu.Discriminator,
                        Price = x.Price,
                    })
                    .ToList();

        }

        public decimal GetTotalValue()
        {
            return _set
                .AsQueryable()
                .Where(x => x.ClaimerId != null)
                .Sum(x => x.Price);
        }

        public int AffinityCount(ulong userId)
        {
            //return _context.Set<WaifuUpdate>()
            //           .Count(w => w.User.UserId == userId &&
            //               w.UpdateType == WaifuUpdateType.AffinityChanged &&
            //               w.New != null));
            if (_context.Database.IsNpgsql())
            {
                return _context.Set<WaifuUpdate>()
                    .FromSqlInterpolated($@"SELECT 1 
                            FROM ""WaifuUpdates""
                            WHERE ""UserId"" = (SELECT ""Id"" from ""DiscordUser"" WHERE ""UserId""={userId}) AND 
                            ""UpdateType"" = 0 AND 
                            ""NewId"" IS NOT NULL")
                    .Count();
            }

            if (_context.Database.IsSqlServer())
            {
                return _context.Set<WaifuUpdate>()
                    .FromSqlInterpolated($@"SELECT 1 
                            FROM WaifuUpdates
                            WHERE UserId = (SELECT Id from DiscordUser WHERE UserId={userId}) AND 
                            UpdateType = 0 AND 
                            NewId IS NOT NULL")
                    .Count();
            }

            return default(int);
        }

        public WaifuInfoStats GetWaifuInfo(ulong userId)
        {
            if (_context.Database.IsNpgsql())
            {
                _context.Database.ExecuteSqlInterpolated($@"
                    INSERT INTO ""WaifuInfo"" (""AffinityId"", ""ClaimerId"", ""Price"", ""WaifuId"")
                    VALUES ({null}, {null}, {1}, (SELECT ""Id"" FROM ""DiscordUser"" WHERE ""UserId""={userId}))
                    ON CONFLICT (""WaifuId"") DO NOTHING;");
            }

            if (_context.Database.IsSqlServer())
            {
                IQueryable<DiscordUser> userQueryable = _context.Set<DiscordUser>()
                    .FromSqlInterpolated($@"SELECT * FROM DiscordUser WHERE UserId={userId}");

                DiscordUser user = userQueryable.FirstOrDefault();
                if (user != null)
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                        INSERT INTO WaifuInfo WITH (ROWLOCK) (AffinityId, ClaimerId, Price, WaifuId)
                        SELECT null, null, 1, {user.Id}
                        WHERE NOT EXISTS (SELECT * FROM WaifuInfo WHERE WaifuId={user.Id});");
                }
            }

            var toReturn = _set.AsQueryable()
                .Where(w => w.WaifuId == _context.Set<DiscordUser>()
                    .AsQueryable()
                    .Where(u => u.UserId == userId)
                    .Select(u => u.Id).FirstOrDefault())
                .Select(w => new WaifuInfoStats
                {
                    FullName = _context.Set<DiscordUser>()
                        .AsQueryable()
                        .Where(u => u.UserId == userId)
                        .Select(u => u.Username + "#" + u.Discriminator)
                        .FirstOrDefault(),

                    AffinityCount = _context.Set<WaifuUpdate>()
                        .AsQueryable()
                        .Where(x => x.UserId == w.WaifuId &&
                            x.UpdateType == WaifuUpdateType.AffinityChanged &&
                            x.NewId != null)
                        .Count(),

                    AffinityName = _context.Set<DiscordUser>()
                        .AsQueryable()
                        .Where(u => u.Id == w.AffinityId)
                        .Select(u => u.Username + "#" + u.Discriminator)
                        .FirstOrDefault(),

                    ClaimCount = _set.AsQueryable()
                        .Where(x => x.ClaimerId == w.WaifuId)
                        .Count(),

                    ClaimerName = _context.Set<DiscordUser>()
                        .AsQueryable()
                        .Where(u => u.Id == w.ClaimerId)
                        .Select(u => u.Username + "#" + u.Discriminator)
                        .FirstOrDefault(),

                    DivorceCount = _context.Set<WaifuUpdate>()
                        .AsQueryable()
                        .Where(x => x.OldId == w.WaifuId &&
                            x.NewId == null &&
                            x.UpdateType == WaifuUpdateType.Claimed)
                            .Count(),

                    Price = w.Price,

                    Claims30 = _set
                        .AsQueryable()
                        .Include(x => x.Waifu)
                        .Where(x => x.ClaimerId == w.WaifuId)
                        .Select(x => x.Waifu.Username + "#" + x.Waifu.Discriminator)
                        .OrderBy(x => Guid.NewGuid())
                        .Take(30)
                        .ToList(),

                    Items = _context.Set<WaifuItem>()
                        .AsQueryable()
                        .Where(x => x.WaifuInfoId == w.Id)
                        .ToList(),
                })
            .FirstOrDefault();
            if (toReturn is null)
                return null;
            
            toReturn.Claims30 = toReturn.Claims30 is null
                ? new List<string>()
                : toReturn.Claims30.OrderBy(x => Guid.NewGuid()).Take(30).ToList(); 
            
            return toReturn;
        }
    }
}
