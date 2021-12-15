using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Base.Models
{
    /// <summary>
    /// <see cref="DbContext"/> For flip tracking
    /// </summary>
    public class BaseDbContext : DbContext
    {
        public DbSet<Flip> Flips { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="BaseDbContext"/>
        /// </summary>
        /// <param name="options"></param>
        public BaseDbContext(DbContextOptions<BaseDbContext> options)
        : base(options)
        {
        }

        /// <summary>
        /// Configures additional relations and indexes
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Flip>(entity =>
            {
                entity.HasIndex(e => new { e.AuctionId });
            });
        }
    }
}