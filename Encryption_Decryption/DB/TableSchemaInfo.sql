CREATE TABLE TableSchemaInfo (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TableName NVARCHAR(MAX),
    ColumnName NVARCHAR(MAX),
    DataType NVARCHAR(MAX),
    IsNullable BIT,
    DefaultValue NVARCHAR(MAX),
    FullType NVARCHAR(MAX)
);
