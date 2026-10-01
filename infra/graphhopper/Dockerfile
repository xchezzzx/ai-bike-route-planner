# syntax=docker/dockerfile:1
FROM maven:3.9.11-eclipse-temurin-25@sha256:407c4423cec0cf2981055bc2c6c0dc211d9605b6669279b95997f2d1c7e91e2c AS artifact
RUN mvn -B org.apache.maven.plugins:maven-dependency-plugin:3.8.1:copy \
    -Dartifact=com.graphhopper:graphhopper-web:11.1:jar \
    -DoutputDirectory=/engine && \
    echo '8462f758d9ea49edaded557cec5c687a0a24f004ec371cd4daaeb0534824ea33  /engine/graphhopper-web-11.1.jar' | sha256sum -c -

FROM eclipse-temurin:25-jre@sha256:8da0490fa9a3c26867012019565948eef0ee69438f5c75ac28146967bae984b5
LABEL org.opencontainers.image.title="Local GraphHopper road engine" \
      org.opencontainers.image.version="11.1" \
      org.opencontainers.image.source="https://github.com/graphhopper/graphhopper" \
      org.opencontainers.image.revision="ebb578f4e94db72e82f2f8d3bc69417758e16e1c"
WORKDIR /opt/graphhopper
RUN apt-get update && apt-get install -y --no-install-recommends curl=8.18.0-1ubuntu2.7 && \
    rm -rf /var/lib/apt/lists/*
COPY --from=artifact /engine/graphhopper-web-11.1.jar ./graphhopper.jar
COPY config.yml road.json entrypoint.sh ./
RUN chmod 755 entrypoint.sh && java -version && command -v curl && command -v flock
EXPOSE 8989
ENTRYPOINT ["/opt/graphhopper/entrypoint.sh"]
