using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ReversesTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_AmountMax", "\"Amount\" <= 1000000000");
                    table.CheckConstraint("CK_Transactions_AmountPositive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Transactions_NotSelfReversing", "\"ReversesTransactionId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Reverses",
                        column: x => x.ReversesTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Account_Category",
                table: "Transactions",
                columns: new[] { "AccountId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Account_Occurred",
                table: "Transactions",
                columns: new[] { "AccountId", "OccurredAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferId",
                table: "Transactions",
                column: "TransferId",
                filter: "\"TransferId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_Reverses",
                table: "Transactions",
                column: "ReversesTransactionId",
                unique: true,
                filter: "\"ReversesTransactionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
