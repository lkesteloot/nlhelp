
APP=program

run: $(APP).exe
	mono $(APP).exe

%.exe: %.fs
	fsharpc $< -r:Npgsql.dll
