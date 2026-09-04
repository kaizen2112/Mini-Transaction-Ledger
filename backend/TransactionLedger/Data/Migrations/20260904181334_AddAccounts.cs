using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_BalanceNonNegative", "\"Balance\" >= 0");
                    table.ForeignKey(
                        name: "FK_Accounts_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId");

            // BR-14: account names are unique per user, case-insensitively,
            // after trimming. This is a FUNCTIONAL index -- the uniqueness is
            // over the expression LOWER("Name"), not over the stored column --
            // and EF Core's fluent API cannot express that, so it is raw SQL
            // (docs/04-database-design.md §3.2). It is also what enforces the
            // rule: the service attempts the insert and translates the
            // violation rather than pre-checking (BR-11).
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "UX_Accounts_User_NameLower"
                    ON "Accounts" ("UserId", LOWER("Name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // UX_Accounts_User_NameLower needs no explicit drop: dropping the
            // table takes its indexes with it.
            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
