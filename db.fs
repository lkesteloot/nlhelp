// Copyright 2014 Lawrence Kesteloot

// Database methods.
module Db

open System
open System.Data
open Npgsql

// Converts a data reader to a sequence of IDataRecord objects.
// Closes the reader when done.
let readerAsSeq (reader:IDataReader) =
    seq {
        // Auto-close.
        use closingReader = reader
        while closingReader.Read() do yield closingReader :> IDataRecord
    }

let executeCommand (dbcon:IDbConnection) command =
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- command
    dbcmd.ExecuteNonQuery() |> ignore

// Numbered from 1.
let private upgrades = [
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

// Takes a query that should return one row with one column, where
// the single cell is an integer, and returns that integer.
let private queryInteger (dbcon:IDbConnection) query =
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- query
    int (dbcmd.ExecuteScalar() :?> Int32)

// Add a parameter to a command. Call this for every "@Param" in the query.
// Do not include the "@" in the name.
let addParameter (dbcmd:IDbCommand) name value =
    let param = dbcmd.CreateParameter()
    param.ParameterName <- name
    param.Value <- value
    dbcmd.Parameters.Add(param) |> ignore

// Add a row to the schema tracker with the specified version number.
let private addSchemaTrackerVersion (dbcon:IDbConnection) upgradeId =
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "INSERT INTO schema_tracker VALUES (@Id, @Description)"
    addParameter dbcmd "Id" upgradeId
    addParameter dbcmd "Description" "No description"
    dbcmd.ExecuteNonQuery() |> ignore

// Apply the specified upgrade. The ID is the number, and "upgrade" is
// a function that takes a connection and applies an upgrade.
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

