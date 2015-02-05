namespace FSharp.Data

type EnumMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", ConnectionStrings.LocalDbDefault, CLIEnum = true>

module EnumTests = 

    open System
    open Xunit
    
    [<Literal>]
    let connectionString = ConnectionStrings.LocalDbDefault

    type TinyIntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", connectionString>

    [<Fact>]
    let tinyIntMapping() = 
        Assert.Equal<(string * byte) seq>([| "One", 1uy; "Two", 2uy |], TinyIntMapping.Items)

    [<Fact>]
    let parse() = 
        Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("one", ignoreCase = true))
        Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One", ignoreCase = false))
        Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One"))
        Assert.Throws<ArgumentException>(Assert.ThrowsDelegateWithReturn(fun() -> box (TinyIntMapping.Parse("blah-blah")))) |> ignore
        Assert.Throws<ArgumentException>(Assert.ThrowsDelegateWithReturn(fun() -> box (TinyIntMapping.Parse("one")))) |> ignore

    [<Fact>]
    let Enums() = 
        let succ, result = EnumMapping.TryParse("One")
        Assert.True succ
        Assert.Equal(EnumMapping.One, result)

        Assert.Equal(1, int EnumMapping.One)
        Assert.True(EnumMapping.One = (Enum.Parse(typeof<EnumMapping>, "One") |> unbox))
        Assert.Equal(enum 1, EnumMapping.One)

    [<Fact>]
    let Name() = 
        let value = TinyIntMapping.One
        Assert.Equal(Some "One", TinyIntMapping.TryFindName value)
        Assert.Equal(None, TinyIntMapping.TryFindName Byte.MinValue)