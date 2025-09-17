# OneWayGateway
This gateway forwards https(s) requests received via a 1 way / ipv4 UDP only communication link.

To send the http(s) requests across, the client application and/or the device network stack, must utf8 encode the request into an ipv4 UDP packet and send it via the one way communication link to the gateway running on a device on the other side of the link.

For .net we have a client library that does the above for you.

Check the [wiki](wiki) for more.
