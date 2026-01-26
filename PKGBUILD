pkgname="dropme"
pkgver="1.0.0"
pkgrel="1"
pkgdesc="Peer to peer secure file transfer tool"
arch=("x86_64")
depends=("dotnet-host" "dotnet-runtime")
makedepends=("dotnet-sdk" "git")
source=("git+https://gitlab.scss.tcd.ie/sweng26_group3/sweng26_group3.git")
sha256sums=("SKIP")
license=("custom")

build() {
    cd ${srcdir}/sweng26_group3/DropMe.Desktop
    dotnet publish -c release --os linux --arch x64
}

package() {
    depends=("libx11" "libice" "libsm" "fontconfig")

    mkdir -p ${pkgdir}/usr/lib/DropMe
    cp ${srcdir}/sweng26_group3/DropMe.Desktop/bin/release/net10.0/linux-x64/publish/*.dll ${pkgdir}/usr/lib/DropMe/

    mkdir -p ${pkgdir}/usr/bin
    echo "#!/usr/bin/env sh" > ${pkgdir}/usr/bin/dropme
    echo "dotnet /usr/lib/DropMe/DropMe.Desktop.dll" >> ${pkgdir}/usr/bin/dropme
    chmod +x ${pkgdir}/usr/bin/dropme
}