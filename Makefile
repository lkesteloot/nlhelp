
.PHONY: run importdb

run: nlhelp.exe
	mono nlhelp.exe

nlhelp.exe: nlhelp.fs
	fsharpc db.fs nlhelp.fs -r:Npgsql.dll

importdb: importdb.exe
	mono importdb.exe

importdb.exe: importdb.fs
	fsharpc db.fs importdb.fs -r:Npgsql.dll
