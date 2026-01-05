FROM eclipse-temurin:17-jdk-jammy AS build
WORKDIR /src
COPY SimpleAPIGateway.java .
RUN javac SimpleAPIGateway.java

FROM eclipse-temurin:17-jre-jammy
WORKDIR /app
COPY --from=build /src/*.class /app/
EXPOSE 8443
ENTRYPOINT ["java", "SimpleAPIGateway"]
