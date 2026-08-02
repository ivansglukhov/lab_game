@echo off
dotnet run --project Content.Server -- --cvar config.presets=Nii/nii --cvar game.lobbyduration=10 --cvar game.role_timers=true
pause
