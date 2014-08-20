open System
open System.Net
open System.Text
open System.IO
open System.Data
open Npgsql

let staticRoot = @"static"
let host = "http://localhost:8080/"

// Converts a null string to an empty string.
let notNullString = function
    | null -> ""
    | s -> s

// Escape a JSON string.
let escapeJson (s:string) =
    s.Replace("\\", "\\\\").
        Replace("\"", "\\\"").
        Replace("/", "\\/").
        Replace("\b", "\\b").
        Replace("\n", "\\n").
        Replace("\r", "\\r").
        Replace("\t", "\\t")

// Removes possible ? and subsequent text.
let stripQuery (path:string) =
    match path.IndexOf("?") with
    | -1 -> path
    | index -> path.Substring(0, index)

// Returns a redirection.
let handleRedirection path =
    printfn "Redirecting to %s" path
    (int HttpStatusCode.Redirect, "", path)

// Database methods.
module Db =
    let private executeCommand (dbcon:IDbConnection) command =
        let dbcmd = dbcon.CreateCommand()
        dbcmd.CommandText <- command
        dbcmd.ExecuteNonQuery() |> ignore

    // Numbered from 1.
    let upgrades = [
        fun dbcon -> () // 1
        fun dbcon -> // 2
            executeCommand dbcon "CREATE TABLE answer ( \
                                      id serial PRIMARY KEY, \
                                      answer_text text NOT NULL \
                                  )"
            executeCommand dbcon "CREATE TABLE question ( \
                                      id serial PRIMARY KEY, \
                                      question_text text NOT NULL, \
                                      answer_id integer NOT NULL REFERENCES answer ON DELETE CASCADE

                                  )"
    ]

    let private queryInteger (dbcon:IDbConnection) query =
        let dbcmd = dbcon.CreateCommand()
        dbcmd.CommandText <- query
        int (dbcmd.ExecuteScalar() :?> Int32)

    let private addParameter (dbcmd:IDbCommand) name value =
        let param = dbcmd.CreateParameter()
        param.ParameterName <- name
        param.Value <- value
        dbcmd.Parameters.Add(param) |> ignore

    let private addSchemaTrackerVersion (dbcon:IDbConnection) upgradeId =
        let dbcmd = dbcon.CreateCommand()
        dbcmd.CommandText <- "INSERT INTO schema_tracker VALUES (@Id, @Description)"
        addParameter dbcmd "Id" upgradeId
        addParameter dbcmd "Description" "No description"
        dbcmd.ExecuteNonQuery() |> ignore

    let private applyUpgrade (dbcon:IDbConnection) (upgradeId, upgrade) =
        printfn "Apply schema upgrade %d" upgradeId
        // Do this in a transaction so that if our DDL commands are wrong, we don't
        // have to clean up while debugging.
        let dbtx = dbcon.BeginTransaction()
        upgrade dbcon
        addSchemaTrackerVersion dbcon upgradeId
        dbtx.Commit()

    let private createTables (dbcon:IDbConnection) =
        // Create the schema tracker if it doesn't yet exist.
        try
            // This will throw if it already exists.
            executeCommand dbcon "CREATE TABLE schema_tracker ( \
                                      id integer, \
                                      description text \
                                  )"
            // If we created the table, then seed it.
            executeCommand dbcon "INSERT INTO schema_tracker VALUES (1, 'Empty schema')"
        with
        | :? Npgsql.NpgsqlException -> ()

        let schemaVersion = queryInteger dbcon "SELECT max(id) FROM schema_tracker"
        printfn "Schema version: %d" schemaVersion

        // Apply all the upgrades that we've not done yet.
        upgrades
            |> Seq.zip [1 .. (List.length upgrades)]
            |> Seq.skip schemaVersion
            |> Seq.iter (applyUpgrade dbcon)

    // % sudo -u postgres psql
    // postgres=# create role nlhelp nosuperuser nocreatedb nocreaterole inherit login;
    // postgres=# create database nlhelp owner nlhelp encoding 'unicode';
    let private connectionString = "Server=localhost;Database=nlhelp;User ID=nlhelp"
    let makeConnection =
        let dbcon = new NpgsqlConnection(connectionString)
        dbcon.Open()
        createTables dbcon
        dbcon

// Handling of static files.
module Static =
    let private detectMimeType (pathname:string) =
        match (Path.GetExtension(pathname).ToLower()) with
        | ".html" -> "text/html"
        | ".js" -> "text/javascript" // "application/javascript" is correct but chokes IE.
        | ".css" -> "text/css"
        | ".jpg" | ".jpeg" -> "image/jpeg"
        | ".gif" -> "image/gif"
        | ".png" -> "image/png"
        | _ ->
            printfn "WARNING: Unknown extension for file %s" pathname
            "text/plain"

    let private handleStaticFile (pathname:string) =
        if (File.Exists pathname)
            then (int HttpStatusCode.OK, detectMimeType pathname, File.ReadAllText(pathname))
            else (int HttpStatusCode.NotFound, "text/plain", "Page does not exist.")

    let rec private handleStaticDirectory (path:string) =
        if (path.EndsWith("/"))
            then handleRequest (path + "index.html")
            else handleRedirection (path + "/")

    and handleRequest path =
        // Force it relative by adding "." in front:
        let pathname = Path.Combine(staticRoot, "." + path)
        printfn "Static file: %s" pathname
        if (Directory.Exists pathname)
            then handleStaticDirectory path
            else handleStaticFile pathname

// Handling of search requests.
module Search =
    type SearchResponse = {
        Query: string
        Text: string
    }

    // Convert a response to JSON.
    let makeJsonSearchResponse (response:SearchResponse) =
        @"{""query"":""" + escapeJson response.Query +
            @""", ""text"":""" + escapeJson response.Text + @"""}"

    // Handle queries to the /search URL.
    let handleRequest dbcon query =
        printfn "Query: %s" query
        let response = {
            Query = query
            Text = "# Official answer\nYou are clearly *stupid*."
        }
        (200, "application/json", makeJsonSearchResponse response)

module Website =
    // Handle GET requests.
    let private handleGetRequest dbcon (req:HttpListenerRequest) =
        match stripQuery req.RawUrl with
        | "/search" -> Search.handleRequest dbcon (notNullString (req.QueryString.Get("q")))
        | path -> Static.handleRequest path

    // Generic request handler.
    let handleRequest dbcon (req:HttpListenerRequest) =
        match req.HttpMethod.ToUpper() with
        | "GET" -> handleGetRequest dbcon req
        | _ -> (405, "text/plain", "Method not allowed")

module Http =
    // Write the response body.
    let private writeBody (resp:HttpListenerResponse) (statusCode:int) (contentType:string) (body:string) =
        let asciiBody = Encoding.ASCII.GetBytes(body)
        resp.StatusCode <- statusCode
        resp.ContentType <- contentType
        resp.ContentLength64 <- int64 asciiBody.Length
        resp.OutputStream.Write(asciiBody, 0, asciiBody.Length)
        resp.OutputStream.Close()

    // 302 Redirect to the location.
    let private redirectTo (resp:HttpListenerResponse) location =
        resp.Redirect(location)
        resp.OutputStream.Close()

    // Register a listener for HTTP requests.
    let registerListener (handler:(HttpListenerRequest->HttpListenerResponse->Async<unit>)) =
        let hl = new HttpListener()
        hl.Prefixes.Add host
        hl.Start()
        let task = Async.FromBeginEnd(hl.BeginGetContext, hl.EndGetContext)
        async {
            while true do
                let! context = task
                Async.Start(handler context.Request context.Response)
        } |> Async.Start

    // Listen for requests and create a response.
    let listener dbcon req resp =
        async {
            let statusCode, contentType, body = Website.handleRequest dbcon req
            printfn "%d %s %s" statusCode contentType req.RawUrl
            if statusCode >= 300 && statusCode < 400
                then redirectTo resp body
                else writeBody resp statusCode contentType body 
        }

[<EntryPoint>]
let main argv =
    let dbcon = Db.makeConnection
    Http.registerListener (Http.listener dbcon)
    Console.ReadLine() |> ignore
    dbcon.Close()
    0
