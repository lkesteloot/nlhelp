
.PHONY: run importdb

run: nlhelp.exe
	mono nlhelp.exe

nlhelp.exe: db.fs nlp.fs nlhelp.fs
	fsharpc db.fs nlp.fs nlhelp.fs -r:Npgsql.dll

importdb: importdb.exe
	mono importdb.exe

importdb.exe: importdb.fs
	fsharpc db.fs importdb.fs -r:Npgsql.dll
