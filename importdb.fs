// Copyright 2014 Lawrence Kesteloot

open System.Data

// Represents an entry in the FAQ.
type Entry = { question : string; answer : string; }

// Returns a list of Entry objects.
let readEntries pathname =
    // Accumulate the entries as we go. If it's a new question, we prepend a new
    // entry. Otherwise we just add the line to the existing head.
    let parseLine entries (line:string) =
        if (line.StartsWith("# "))
            then
                // New question.
                let question = (line.Substring(2))
                { question = question; answer = line; } :: entries
            else
                // Append to existing question.
                match entries with
                | entry :: rest ->
                    { question = entry.question; answer = entry.answer + "\n" + line; } :: rest
                | _ -> [] // Junk at the beginning of the document.

    System.IO.File.ReadLines(pathname)
        |> List.ofSeq
        |> List.fold parseLine []
        |> List.rev

// Removes all questions and answers from the database.
let clearDatabase (dbcon:IDbConnection) =
    Db.executeCommand dbcon "DELETE FROM answer"
    Db.executeCommand dbcon "DELETE FROM question"

// Imports an Entry object into the database.
let importEntry (dbcon:IDbConnection) entry =
    // Insert the answer and get its ID.
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "INSERT INTO answer (answer_text) VALUES (@AnswerText) RETURNING id"
    Db.addParameter dbcmd "AnswerText" entry.answer
    let answerId = dbcmd.ExecuteScalar()

    // Insert the question and refer to the answer ID.
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "INSERT INTO question (question_text, answer_id) VALUES (@QuestionText, @AnswerId)"
    Db.addParameter dbcmd "QuestionText" entry.question
    Db.addParameter dbcmd "AnswerId" answerId
    dbcmd.ExecuteNonQuery() |> ignore

    printfn "Inserted: %s" entry.question

// Imports a list of Entry objects into the database.
let importEntries (dbcon:IDbConnection) entries =
    entries |> List.iter (importEntry dbcon)

[<EntryPoint>]
let main argv =
    let dbcon = Db.makeConnection
    let entries = readEntries "db"
    clearDatabase dbcon
    importEntries dbcon entries
    0

