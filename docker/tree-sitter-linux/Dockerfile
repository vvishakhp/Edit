FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    curl \
    ca-certificates \
    build-essential \
    g++ \
    nodejs \
    npm \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g tree-sitter-cli@0.26.13

WORKDIR /repo
COPY scripts/build-tree-sitter.sh /repo/scripts/build-tree-sitter.sh
COPY docker/tree-sitter-linux/build.sh /build/build.sh
RUN chmod +x /repo/scripts/build-tree-sitter.sh /build/build.sh

ENTRYPOINT ["/build/build.sh"]
