using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    public partial class RemoveMembershipId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MembershipId",
                table: "Customers");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MembershipId",
                table: "Customers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
