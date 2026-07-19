# SSH reverse-tunnel sidecar (mobile-app / remote-access track, Этап 3c).
# Mirrors the owner's HA autossh addon: a permanent reverse tunnel from the home stack to a VPS, so the home is
# reachable even behind CGNAT / without IPv6. Alpine + autossh + the openssh client, nothing else.
FROM alpine:3.20

RUN apk add --no-cache autossh openssh-client bash

COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
