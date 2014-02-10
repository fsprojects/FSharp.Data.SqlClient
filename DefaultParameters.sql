exec sp_executesql N'SELECT
param.name AS [Name],
param.parameter_id AS [ID],
param.default_value AS [DefaultValue],
param.has_default_value AS [HasDefaultValue],
usrt.name AS [DataType],
s1param.name AS [DataTypeSchema],
ISNULL(baset.name, N'''') AS [SystemType],
CAST(CASE WHEN baset.name IN (N''nchar'', N''nvarchar'') AND param.max_length <> -1 THEN param.max_length/2 ELSE param.max_length END AS int) AS [Length],
CAST(param.precision AS int) AS [NumericPrecision],
CAST(param.scale AS int) AS [NumericScale],
ISNULL(xscparam.name, N'''') AS [XmlSchemaNamespace],
ISNULL(s2param.name, N'''') AS [XmlSchemaNamespaceSchema],
ISNULL( (case param.is_xml_document when 1 then 2 else 1 end), 0) AS [XmlDocumentConstraint],
CASE WHEN usrt.is_table_type = 1 THEN N''structured'' ELSE N'''' END AS [UserType],
param.is_output AS [IsOutputParameter],
param.is_cursor_ref AS [IsCursorParameter],
param.is_readonly AS [IsReadOnly],
sp.object_id AS [IDText],
db_name() AS [DatabaseName],
param.name AS [ParamName],
CAST(
 case 
    when sp.is_ms_shipped = 1 then 1
    when (
        select 
            major_id 
        from 
            sys.extended_properties 
        where 
            major_id = sp.object_id and 
            minor_id = 0 and 
            class = 1 and 
            name = N''microsoft_database_tools_support'') 
        is not null then 1
    else 0
end          
             AS bit) AS [ParentSysObj],
1 AS [Number]
FROM
sys.all_objects AS sp
INNER JOIN sys.all_parameters AS param ON param.object_id=sp.object_id
LEFT OUTER JOIN sys.types AS usrt ON usrt.user_type_id = param.user_type_id
LEFT OUTER JOIN sys.schemas AS s1param ON s1param.schema_id = usrt.schema_id
LEFT OUTER JOIN sys.types AS baset ON (baset.user_type_id = param.system_type_id and baset.user_type_id = baset.system_type_id) or ((baset.system_type_id = param.system_type_id) and (baset.user_type_id = param.user_type_id) and (baset.is_user_defined = 0) and (baset.is_assembly_type = 1)) 
LEFT OUTER JOIN sys.xml_schema_collections AS xscparam ON xscparam.xml_collection_id = param.xml_collection_id
LEFT OUTER JOIN sys.schemas AS s2param ON s2param.schema_id = xscparam.schema_id
WHERE
(param.name=@_msparam_0)and((sp.type = @_msparam_1 OR sp.type = @_msparam_2 OR sp.type=@_msparam_3)and(sp.name=@_msparam_4 and SCHEMA_NAME(sp.schema_id)=@_msparam_5))',N'@_msparam_0 nvarchar(4000),@_msparam_1 nvarchar(4000),@_msparam_2 nvarchar(4000),@_msparam_3 nvarchar(4000),@_msparam_4 nvarchar(4000),@_msparam_5 nvarchar(4000)'
,@_msparam_0=N'@ErrorLogID',@_msparam_1=N'P',@_msparam_2=N'RF',@_msparam_3=N'PC',@_msparam_4=N'uspLogError',@_msparam_5=N'dbo'