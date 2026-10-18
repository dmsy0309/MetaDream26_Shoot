@echo off
rem Allow the Metadream Shoot server through Windows Defender Firewall (run as Administrator).
rem TCP 7777 = game connections, UDP 47777 = LAN room discovery.
netsh advfirewall firewall add rule name="Metadream Shoot Server TCP" dir=in action=allow protocol=TCP localport=7777
netsh advfirewall firewall add rule name="Metadream Shoot Server LAN" dir=in action=allow protocol=UDP localport=47777
pause
