ARG TEST_BASE_IMAGE=udptohttpgateway:publish

FROM udptohttpgateway:publish AS test
ARG TARGETARCH
WORKDIR /src
#copying the rest of files to get the tests and dependencies
COPY . . 
RUN cd /app/publish && (./UdpToHttpGateway&) && cd /src/UdpToHttpGateway.Tests && \
    dotnet test -a $TARGETARCH -c $BUILD_CONFIGURATION --blame --blame-hang --blame-hang-timeout 10min
