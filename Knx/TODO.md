# TODO

## KNX Group Messages receiving/sending

- [ ] Check whether sending Group Message is done via SRF.Network.Udp.IUdpMessageQueue and whether a there's a queue processor in the AddKnx... hosting extion methods.
- [ ] Check whether receiving Group Messages is done via SRF.Network.Udp.IUdpMulticastClient and whether there's a background service in the AddKnx... hosting extension methods that listens to the multicast group and raises the KnxBus.GroupMessageReceived event.
- [ ] Add rate limiting to the send queue to avoid flooding the bus in case of a misbehaving consumer. This can be done via a simple timer that allows sending one message per configured interval (e.g. 100ms) and drops or delays messages that exceed this rate. Ensure received telegrams contribute to the total bus rate / load calculation and rate limiting (combined send/read rate limiting whereas read cannot be influenced).
- [ ] Add unit tests for sending and receiving group messages, including edge cases like invalid messages, high load, and connection issues.
- [ ] Add integration tests that use a real or simulated KNX/IP routing connection to verify end-to-end functionality of sending and receiving group messages.