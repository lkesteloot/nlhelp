// Copyright 2014 Lawrence Kesteloot

open System
open System.Net
open System.Text
open System.IO

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
        printfn "Listening on %s" host

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
