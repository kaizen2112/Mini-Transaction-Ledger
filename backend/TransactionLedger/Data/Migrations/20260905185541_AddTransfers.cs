using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DebitTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.CheckConstraint("CK_Transfers_AmountPositive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Transfers_DifferentAccounts", "\"SourceAccountId\" <> \"DestinationAccountId\"");
                    table.ForeignKey(
                        name: "FK_Transfers_CreditTransactions",
                        column: x => x.CreditTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_DebitTransactions",
                        column: x => x.DebitTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_DestinationAccounts",
                        column: x => x.DestinationAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_SourceAccounts",
                        column: x => x.SourceAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_DestinationAccountId",
                table: "Transfers",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceAccountId",
                table: "Transfers",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_UserId",
                table: "Transfers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_Transfers_CreditTx",
                table: "Transfers",
                column: "CreditTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Transfers_DebitTx",
                table: "Transfers",
                column: "DebitTransactionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transfers",
                table: "Transactions",
                column: "TransferId",
                principalTable: "Transfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transfers",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Transfers");
        }
    }
}
