using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace CashSloth.Server.Data.Migrations;

[DbContext(typeof(ServerDbContext))]
[Migration("20260821120000_ServerEventsV15")]
public partial class ServerEventsV15 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                HostUserId = table.Column<string>(type: "TEXT", nullable: false),
                HostNickname = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                PresetId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                PresetVersion = table.Column<long>(type: "INTEGER", nullable: false),
                PresetHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PresetSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                JoinMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                JoinCodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                RulesJson = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                SalesCutoffUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                FinalReportJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
                table.ForeignKey(
                    name: "FK_Events_AspNetUsers_HostUserId",
                    column: x => x.HostUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "EventMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Nickname = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                NicknameNormalized = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                JoinedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LeftAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                KickedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                PendingSaleCount = table.Column<int>(type: "INTEGER", nullable: false),
                SynchronisedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EventMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_EventMembers_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_EventMembers_Devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_EventMembers_Events_EventId",
                    column: x => x.EventId,
                    principalTable: "Events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EventSales",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                IsShowcase = table.Column<bool>(type: "INTEGER", nullable: false),
                SubtotalCents = table.Column<long>(type: "INTEGER", nullable: false),
                TipCents = table.Column<long>(type: "INTEGER", nullable: false),
                TotalCents = table.Column<long>(type: "INTEGER", nullable: false),
                GivenCents = table.Column<long>(type: "INTEGER", nullable: false),
                ChangeCents = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EventSales", x => x.Id);
                table.ForeignKey(
                    name: "FK_EventSales_EventMembers_MemberId",
                    column: x => x.MemberId,
                    principalTable: "EventMembers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_EventSales_Events_EventId",
                    column: x => x.EventId,
                    principalTable: "Events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EventSaleLines",
            columns: table => new
            {
                SaleId = table.Column<string>(type: "TEXT", nullable: false),
                LineIndex = table.Column<int>(type: "INTEGER", nullable: false),
                ItemId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                UnitCents = table.Column<long>(type: "INTEGER", nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                LineTotalCents = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EventSaleLines", x => new { x.SaleId, x.LineIndex });
                table.ForeignKey(
                    name: "FK_EventSaleLines_EventSales_SaleId",
                    column: x => x.SaleId,
                    principalTable: "EventSales",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_Events_HostUserId_State", table: "Events", columns: new[] { "HostUserId", "State" });
        migrationBuilder.CreateIndex(name: "IX_Events_State", table: "Events", column: "State");
        migrationBuilder.CreateIndex(name: "IX_EventMembers_DeviceId_Status", table: "EventMembers", columns: new[] { "DeviceId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_EventMembers_EventId_NicknameNormalized", table: "EventMembers", columns: new[] { "EventId", "NicknameNormalized" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_EventMembers_EventId_UserId_DeviceId", table: "EventMembers", columns: new[] { "EventId", "UserId", "DeviceId" });
        migrationBuilder.CreateIndex(name: "IX_EventMembers_UserId", table: "EventMembers", column: "UserId");
        migrationBuilder.CreateIndex(name: "IX_EventSales_EventId_CompletedAtUtc", table: "EventSales", columns: new[] { "EventId", "CompletedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_EventSales_MemberId_CompletedAtUtc", table: "EventSales", columns: new[] { "MemberId", "CompletedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EventSaleLines");
        migrationBuilder.DropTable(name: "EventSales");
        migrationBuilder.DropTable(name: "EventMembers");
        migrationBuilder.DropTable(name: "Events");
    }
}
