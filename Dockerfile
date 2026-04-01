FROM mcr.microsoft.com/dotnet/sdk:10.0

# Set environment variables for Android SDK & Java
ENV ANDROID_HOME=/opt/android-sdk
ENV ANDROID_SDK_ROOT=$ANDROID_HOME
ENV JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
ENV PATH=$PATH:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$JAVA_HOME/bin

RUN apt-get update && \
    apt-get install -y wget unzip openjdk-21-jdk openssh-client && \
    rm -rf /var/lib/apt/lists/*

# Setup Android Command Line Tools
# Organizes the tools into the 'latest' directory structure required by sdkmanager
RUN mkdir -p $ANDROID_HOME/cmdline-tools \
    && wget https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip -O /tmp/cmdline-tools.zip \
    && unzip /tmp/cmdline-tools.zip -d $ANDROID_HOME/cmdline-tools \
    && mv $ANDROID_HOME/cmdline-tools/cmdline-tools $ANDROID_HOME/cmdline-tools/latest \
    && rm /tmp/cmdline-tools.zip

RUN yes | sdkmanager --licenses \
    && sdkmanager "platform-tools" \
    && sdkmanager "platforms;android-36" \
    && sdkmanager "build-tools;36.0.0"