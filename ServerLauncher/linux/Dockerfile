# Container image for the Linux dedicated server. Build from this folder:
#   docker build -t metadream-shoot-server .
#   docker run -d --name metadream -p 7777:7777 metadream-shoot-server
# LAN room discovery (UDP 47777) does not cross Docker's bridge network; players join by IP/port,
# or run with --network host on Linux.
FROM ubuntu:22.04
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --shell /usr/sbin/nologin metadream
WORKDIR /opt/metadream-shoot
COPY . .
RUN chmod +x start-server.sh MetadreamShootServer.x86_64 && chown -R metadream:metadream /opt/metadream-shoot
USER metadream
EXPOSE 7777/tcp
EXPOSE 47777/udp
ENTRYPOINT ["./start-server.sh"]
