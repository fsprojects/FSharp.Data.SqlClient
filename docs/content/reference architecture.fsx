(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"

(**

Beautiful Functional Architectures 
===================

[Project Euler. #17.](https://projecteuler.net/problem=17)

F# solution:
-------------------------------------
*)

let ``1..9`` = 
    [""; "one"; "two"; "three"; "four"; "five"; "six"; "seven"; "eight"; "nine"]
let ``10..19`` =  
    ["ten"; "eleven"; "twelve"; "thirteen"; "fourteen"; "fifteen"; "sixteen"; "seventeen"; "eighteen"; "nineteen"]
let dozens = 
    [""; ""; "twenty"; "thirty"; "forty"; "fifty"; "sixty"; "seventy"; "eighty"; "ninety"]

let spellNumber n = 
    [
        if n = 1000 then yield! "onethousand"
        else 
            let hundredsDigit = n / 100
            if hundredsDigit > 0 then yield! sprintf "%shundred" ``1..9``.[hundredsDigit] 
            let moduloOf100 = n % 100
            if hundredsDigit <> 0 && moduloOf100 <> 0 then yield! "and"
            if moduloOf100 >= 10 && moduloOf100 <= 19 then yield! ``10..19``.[moduloOf100 - 10]
            else
                yield! dozens.[moduloOf100 / 10] + ``1..9``.[moduloOf100 % 10]
    ]

[ 1 .. 1000 ]
|> List.collect spellNumber
|> List.length

(**

T-SQL solution:
-------------------------------------

<pre>
<code lang="sql">
CREATE FUNCTION dbo.SpellNumber
(
	@n AS INT
)
RETURNS @result TABLE
(
	 Chars NVARCHAR(MAX) NOT NULL
)
AS
BEGIN
	IF @n = 1000
		INSERT INTO @result VALUES ('onethousand')
	ELSE
		WITH Digits AS 
		(
			SELECT offset, value
			FROM 
				(VALUES 
					(0, ''),  (1, 'one'),  (2, 'two'),  (3, 'three'),  (4, 'four'),
					(5, 'five'),  (6, 'six'),  (7, 'seven'),  (8, 'eight'),  (9, 'nine')) 
				AS T(offset, value)
		),
		TenToNineteen AS
		(
			SELECT offset, value
			FROM 
				(VALUES 
					(0, 'ten'),  (1, 'eleven'),  (2, 'twelve'),  (3, 'thirteen'),  (4, 'fourteen'),
					(5, 'fifteen'),  (6, 'sixteen'),  (7, 'seventeen'),  (8, 'eighteen'),  (9, 'nineteen')) 
				AS T(offset, value)
		),
		Dozens AS
		(
			SELECT offset, value
			FROM 
				(VALUES 
					(0, ''),  (1, ''),  (2, 'twenty'),  (3, 'thirty'),  (4, 'forty'),
                    (5, 'fifty'),  (6, 'sixty'),  (7, 'seventy'),  (8, 'eighty'),  (9, 'ninety')) 
				AS T(offset, value)
		)
		INSERT INTO @result 
			SELECT value + 'hundred' FROM Digits WHERE @n / 100 > 0 AND offset = @n / 100
			UNION ALL
			SELECT 'and' WHERE @n / 100 > 0 AND @n % 100 > 0 
			UNION ALL
			SELECT value FROM TenToNineteen WHERE (@n % 100) - 10 = offset
			UNION ALL
			SELECT value FROM Dozens WHERE (@n % 100 NOT BETWEEN 10 AND 19) AND ((@n % 100) / 10 = offset) 
			UNION ALL
			SELECT value FROM Digits WHERE (@n % 100 NOT BETWEEN 10 AND 19) AND ((@n % 100) % 10 = offset) 
	RETURN
END
</code>
</pre>

*)


open FSharp.Data

[<Literal>]
let adventureWorks = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>]
let euler17 = "
    WITH InfiniteSeq(N) AS
    (
        SELECT 1
	    UNION ALL
        SELECT 1 + N FROM InfiniteSeq
    )

    SELECT SUM(LEN(ys.Chars))
    FROM
	    (SELECT TOP 1000 * FROM InfiniteSeq ) AS xs
	    CROSS APPLY dbo.SpellNumber(xs.N) AS ys
    OPTION (MAXRECURSION 0)
" 
type Euler17 = SqlCommandProvider<euler17, adventureWorks, SingleRow = true>

Euler17().Execute().Value.Value //21124


