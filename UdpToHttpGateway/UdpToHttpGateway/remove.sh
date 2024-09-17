#!/bin/bash -eu
trap 'echo -e "\033[1;31mScript failed (line $LINENO)!\033[0m"' ERR #general error message in case of error

systemctl stop udptohttpgateway.service
systemctl disable udptohttpgateway.service
rm /etc/systemd/system/udptohttpgateway.service
rm -rf /usr/local/sbin/udptohttpgateway
systemctl daemon-reload
systemctl reset-failed
