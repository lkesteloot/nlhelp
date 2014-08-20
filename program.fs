open System
open System.Net
open System.Text
open System.IO
open Npgsql

let siteRoot = @"static"
let host = "http://localhost:8080/"

let createListener (handler:(HttpListenerRequest->HttpListenerResponse->Async<unit>)) =
    let hl = new HttpListener()
    hl.Prefixes.Add host
    hl.Start()
    let task = Async.FromBeginEnd(hl.BeginGetContext, hl.EndGetContext)
    async {
        while true do
            let! context = task
            Async.Start(handler context.Request context.Response)
    } |> Async.Start

// Converts a null string to an empty string.
let notNullString (s:string) =
    if s = null
        then ""
        else s

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
    let index = path.IndexOf("?")
    if index >= 0
        then path.Substring(0, index)
        else path

// Returns a redirection.
let handleRedirection path =
    printfn "Redirecting to %s" path
    (int HttpStatusCode.Redirect, "", path)

// Handling of static files.
module Static =
    let private detectMimeType (pathname:string) =
        match (Path.GetExtension(pathname).ToLower()) with
        | ".html" -> "text/html"
        | ".js" -> "text/javascript" // "application/javascript" is better but IE chokes.
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
        let pathname = Path.Combine(siteRoot, "." + path)
        printfn "Static file: %s" pathname
        if (Directory.Exists pathname)
            then handleStaticDirectory path
            else handleStaticFile pathname

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
    let handleRequest query =
        printfn "Query: %s" query
        let response = {
            Query = query
            Text = "Hey hey"
        }
        (200, "application/json", makeJsonSearchResponse response)

// Handle GET requests.
let handleGetRequest (req:HttpListenerRequest) =
    let path = stripQuery req.RawUrl
    printfn "Path: %s" path
    if path = "/search"
        then Search.handleRequest (notNullString (req.QueryString.Get("q")))
        else Static.handleRequest path

// Generic request handler.
let handleRequest (req:HttpListenerRequest) =
    if req.HttpMethod.ToUpper() <> "GET"
        then (405, "text/plain", "Method not allowed")
        else handleGetRequest req

// Write the response body.
let writeBody (resp:HttpListenerResponse) (statusCode:int) (contentType:string) (body:string) =
    let asciiBody = Encoding.ASCII.GetBytes(body)
    resp.StatusCode <- statusCode
    resp.ContentType <- contentType
    resp.ContentLength64 <- int64 asciiBody.Length
    resp.OutputStream.Write(asciiBody, 0, asciiBody.Length)
    resp.OutputStream.Close()

let redirectTo (resp:HttpListenerResponse) location =
    resp.Redirect(location)
    resp.OutputStream.Close()

[<EntryPoint>]
let main argv =
    createListener (fun req resp ->
        async {
            let statusCode, contentType, body = handleRequest req
            printfn "%d %s %s" statusCode contentType req.RawUrl
            if statusCode >= 300 && statusCode < 400
                then redirectTo resp body
                else writeBody resp statusCode contentType body 
        })

    Console.ReadLine() |> ignore
    0
