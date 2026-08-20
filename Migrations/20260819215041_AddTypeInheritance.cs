using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMIS_IyaSoft.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentTypeId",
                table: "Types",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TypeId",
                table: "TypePropertyDefinitions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ObjectId",
                table: "ObjectProperties",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Types_ParentTypeId",
                table: "Types",
                column: "ParentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TypePropertyDefinitions_TypeId",
                table: "TypePropertyDefinitions",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectProperties_ObjectId",
                table: "ObjectProperties",
                column: "ObjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Types_Types_ParentTypeId",
                table: "Types",
                column: "ParentTypeId",
                principalTable: "Types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Types_Types_ParentTypeId",
                table: "Types");

            migrationBuilder.DropIndex(
                name: "IX_Types_ParentTypeId",
                table: "Types");

            migrationBuilder.DropIndex(
                name: "IX_TypePropertyDefinitions_TypeId",
                table: "TypePropertyDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ObjectProperties_ObjectId",
                table: "ObjectProperties");

            migrationBuilder.DropColumn(
                name: "ParentTypeId",
                table: "Types");

            migrationBuilder.AlterColumn<string>(
                name: "TypeId",
                table: "TypePropertyDefinitions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "ObjectId",
                table: "ObjectProperties",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
