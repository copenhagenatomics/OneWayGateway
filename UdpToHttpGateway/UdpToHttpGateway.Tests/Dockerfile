ARG TEST_BASE_IMAGE=udptohttpgateway:testbase

FROM ${TEST_BASE_IMAGE} AS test
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN { cd /app/publish && ./UdpToHttpGateway& } && \
    dotnet test -a $TARGETARCH -c $BUILD_CONFIGURATION --no-build --no-restore --blame --blame-hang --blame-hang-timeout 10min
