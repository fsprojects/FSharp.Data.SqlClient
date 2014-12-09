namespace FSharp.Data

open System.Reflection
open System.Collections.Generic
open System.Data
open System.Data.Common
open System
open System.IO
open System.Configuration
open System.Runtime.Caching

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open ProviderImplementation.ProvidedTypes

[<TypeProvider>]
type public SqlEnumProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlEnumProvider", Some typeof<obj>, HideObjectMethods = true, IsErased = false)
    let tempAssembly = ProvidedAssembly( Path.ChangeExtension(Path.GetTempFileName(), ".dll"))
    do tempAssembly.AddTypes [providerType]

    let cache = new MemoryCache(name = this.GetType().Name)

    do 
        this.Disposing.Add(fun _ -> cache.Dispose())

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("Provider", typeof<string>, "System.Data.SqlClient") 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("CLIEnum", typeof<bool>, false) 
            ],             
            instantiationFunction = (fun typeName args ->   
                cache.GetOrAdd(typeName, lazy this.CreateRootType(typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4]))
            )        
        )

        providerType.AddXmlDoc """
<summary>Enumeration based on SQL query.</summary> 
<param name='Query'>SQL used to get the enumeration labels and values. A result set must have at least two columns. The first one is a label.</param>
<param name='ConnectionString'>String used to open a data connection.</param>
<param name='Provider'>Invariant name of a ADO.NET provider. Default is "System.Data.SqlClient".</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
<param name='CLIEnum'>Generate standard CLI Enum. Default is false.</param>
"""

        this.AddNamespace( nameSpace, [ providerType ])
    
    member internal this.CreateRootType( typeName, query, connectionStringOrName, provider, configFile, cliEnum) = 
        let tempAssembly = ProvidedAssembly( Path.ChangeExtension(Path.GetTempFileName(), ".dll"))

        let providedEnumType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes [ providedEnumType ]
        
        let connStr, providerName = SqlEnumProvider.GetConnectionSettings( connectionStringOrName, provider, config.ResolutionFolder, configFile)

        let adoObjectsFactory = DbProviderFactories.GetFactory( providerName: string)
        use conn = adoObjectsFactory.CreateConnection() 
        conn.ConnectionString <- connStr
        conn.Open()

        use cmd = adoObjectsFactory.CreateCommand() 
        cmd.CommandText <- query
        cmd.Connection <- conn
        cmd.CommandType <- CommandType.Text

        use reader = cmd.ExecuteReader()
        if not reader.HasRows then failwith "Resultset is empty. At least one row expected." 
        if reader.FieldCount < 2 then failwith "At least two columns expected in result rowset. Received %i columns." reader.FieldCount
        let schema = reader.GetSchemaTable()

        let valueType, getValue = 
            
            let getValueType(row: DataRow) = 
                let t = Type.GetType( typeName = string row.["DataType"])
                if not( t.IsValueType || t = typeof<string>)
                then 
                    failwithf "Invalid type %s of column %O for value part. Only .NET value types and strings supported as value." t.FullName row.["ColumnName"]
                t

            if schema.Rows.Count = 2
            then
                let valueType = getValueType schema.Rows.[1]
                let getValue (values : obj[]) = values.[0], Expr.Value (values.[0], valueType)

                valueType, getValue
            else
                let tupleItemTypes = 
                    schema.Rows
                    |> Seq.cast<DataRow>
                    |> Seq.skip 1
                    |> Seq.map getValueType
                
                let tupleType = tupleItemTypes |> Seq.toArray |> FSharpType.MakeTupleType
                let getValue = fun (values : obj[]) -> box values, (values, tupleItemTypes) ||> Seq.zip |> Seq.map Expr.Value |> Seq.toList |> Expr.NewTuple
                
                tupleType, getValue

        let names, values = 
            [ 
                while reader.Read() do 
                    let rowValues = Array.zeroCreate reader.FieldCount
                    let count = reader.GetValues( rowValues)
                    assert (count = rowValues.Length)
                    let label = string rowValues.[0]
                    let value = Array.sub rowValues 1 (count - 1) |> getValue
                    yield label, value
            ] 
            |> List.unzip

        names 
        |> Seq.groupBy id 
        |> Seq.iter (fun (key, xs) -> if Seq.length xs > 1 then failwithf "Non-unique label %s." key)

        if cliEnum
        then 
            let allowedTypesForEnum = 
                [| typeof<sbyte>; typeof<byte>; typeof<int16>; typeof<uint16>; typeof<int32>; typeof<uint32>; typeof<int64>; typeof<uint16>; typeof<uint64>; typeof<char> |]
            
            if not(allowedTypesForEnum |> Array.exists valueType.Equals)
            then failwithf "Enumerated types can only have one of the following underlying types: %A." [| for t in allowedTypesForEnum -> t.Name |]

            providedEnumType.SetBaseType typeof<Enum>
            providedEnumType.SetEnumUnderlyingType valueType

            (names, values)
            ||> List.map2 (fun name value -> ProvidedLiteralField(name, providedEnumType, fst value))
            |> providedEnumType.AddMembers

        else
            let valueFields, setFieldValues = 
                (names, values) ||> List.map2 (fun name value -> 
                    let field = ProvidedField( name, valueType)
                    field.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
                    field, Expr.FieldSet(field, snd value)
                ) 
                |> List.unzip

            valueFields |> List.iter providedEnumType.AddMember

            let itemType = FSharpType.MakeTupleType([| typeof<string>; valueType |])
            let seqType = typedefof<_ seq>.MakeGenericType(itemType)
            let itemsField = ProvidedField( "Items", seqType)
            itemsField.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
            providedEnumType.AddMember itemsField 
            
            let itemsExpr = Expr.NewArray(itemType, (names, values) ||> List.map2 (fun name value -> Expr.NewTuple([Expr.Value name; snd value ])))

            let typeInit = ProvidedConstructor([], IsTypeInitializer = true)
            typeInit.InvokeCode <- fun _ -> 
                Expr.Sequential(
                    Expr.FieldSet(itemsField, Expr.Coerce(itemsExpr, seqType)),
                    setFieldValues |> List.reduce (fun x y -> Expr.Sequential(x, y))
                )

            providedEnumType.AddMember typeInit 
            
            do  //TryParse
                let tryParse2Arg = 
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>) // optional=false 
                        ], 
                        returnType = typedefof<_ option>.MakeGenericType( valueType), 
                        IsStaticMethod = true
                    )

                tryParse2Arg.InvokeCode <- 
                    this.GetType()
                        .GetMethod( "GetTryParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| itemsExpr |])
                        |> unbox

                providedEnumType.AddMember tryParse2Arg

                let tryParse1Arg = 
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                        ], 
                        returnType = typedefof<_ option>.MakeGenericType( valueType), 
                        IsStaticMethod = true
                    )

                tryParse1Arg.InvokeCode <- fun args -> Expr.Call(tryParse2Arg, [args.[0]; Expr.Value false])

                providedEnumType.AddMember tryParse1Arg

            do  //Parse
                let parseImpl =
                    this.GetType()
                        .GetMethod( "GetParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| itemsExpr; providedEnumType.FullName |])
                        |> unbox

                let parse2Arg = 
                    ProvidedMethod(
                        methodName = "Parse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>) 
                        ], 
                        returnType = valueType, 
                        IsStaticMethod = true, 
                        InvokeCode = parseImpl
                    )

                providedEnumType.AddMember parse2Arg

                let parse1Arg = 
                    ProvidedMethod(
                        methodName = "Parse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                        ], 
                        returnType = valueType, 
                        IsStaticMethod = true, 
                        InvokeCode = fun args -> Expr.Call(parse2Arg, [args.[0]; Expr.Value false])
                    )

                providedEnumType.AddMember parse1Arg

            do  //TryFindName
                let tryGetName = ProvidedMethod( methodName = "TryFindName", parameters = [ ProvidedParameter("value", valueType) ], returnType = typeof<string option>, IsStaticMethod = true)

                tryGetName.InvokeCode <- 
                    this.GetType()
                        .GetMethod( "GetTryFindName", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| itemsExpr |])
                        |> unbox

                providedEnumType.AddMember tryGetName

        providedEnumType

    //Quotation factories
    
    static member internal GetTryParseImpl<'Value> items = 
        fun (args: _ list) ->
            <@@
                if String.IsNullOrEmpty (%%args.[0]) then nullArg "value"

                let comparer = 
                    if %%args.[1]
                    then StringComparer.InvariantCultureIgnoreCase
                    else StringComparer.InvariantCulture

                %%items |> Array.tryPick (fun (name: string, value: 'Value) -> if comparer.Equals(name, %%args.[0]) then Some value else None) 
            @@>

    static member internal GetParseImpl<'Value>(items, typeName) = 
        fun (args: _ list) ->
            let expr = (SqlEnumProvider.GetTryParseImpl<'Value> (items)) args
            <@@
                match %%expr with
                | Some(x : 'Value) -> x
                | None -> 
                    let errMsg = sprintf @"Cannot convert value ""%s"" to type ""%s""" %%args.[0] typeName
                    invalidArg "value" errMsg
            @@>

    static member internal GetTryFindName<'Value> items = 
        fun (args: _ list) ->
            <@@
                %%items |> Array.tryPick (fun (name: string, value: 'Value) -> if Object.Equals(value, (%%args.[0]: 'Value)) then Some name else None) 
            @@>

    //Config

    static member internal GetConnectionSettings(s: string, provider: string, resolutionFolder, configFile) =
        match s.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
            | [| "" |] -> 
                invalidArg "ConnectionStringOrName" "Value is empty!"

            | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> 
                SqlEnumProvider.ReadConnectionStringSettings( tail.Trim(), resolutionFolder, configFile)
            | _ -> 
                s, provider


    static member internal ReadConnectionStringSettings(name: string, resolutionFolder, fileName) =

        let configFilename = 
            if fileName <> "" 
            then
                let path = Path.Combine(resolutionFolder, fileName)
                if not <| File.Exists path 
                then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
                else path
            else
                let appConfig = Path.Combine(resolutionFolder, "app.config")
                let webConfig = Path.Combine(resolutionFolder, "web.config")

                if File.Exists appConfig then appConfig
                elif File.Exists webConfig then webConfig
                else failwithf "Cannot find either app.config or web.config."
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename
        | _, Lazy x -> 
            let providerName = if String.IsNullOrEmpty x.ProviderName then "System.Data.SqlClient" else x.ProviderName
            x.ConnectionString, providerName