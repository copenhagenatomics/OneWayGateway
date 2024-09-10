#!/bin/bash -eu
trap 'echo -e "\033[1;31mScript failed (line $LINENO)!\033[0m"' ERR #general error message in case of error

cp *.service /etc/systemd/system
mkdir -p /usr/local/sbin/udptohttpgateway
rm -f /usr/local/sbin/udptohttpgateway/UdpToHttpGateway #we explicitely remove to avoid cp of the service failing if its already running
cp UdpToHttpGateway /usr/local/sbin/udptohttpgateway
cp UdpToHttpGateway.dbg /usr/local/sbin/udptohttpgateway
cp appsettings.json /usr/local/sbin/udptohttpgateway
chmod +x /usr/local/sbin/udptohttpgateway/UdpToHttpGateway
systemctl daemon-reload
sudo systemctl start udptohttpgateway.service
sudo systemctl enable udptohttpgateway.service
