using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassHub.Core.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables READ_COMMITTED_SNAPSHOT so readers do not block writers (docs/DATABASE.md).
    /// ALTER DATABASE cannot run inside a transaction, hence <c>suppressTransaction: true</c>.
    /// WITH ROLLBACK IMMEDIATE ends other sessions' open transactions on this database only.
    /// </summary>
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
