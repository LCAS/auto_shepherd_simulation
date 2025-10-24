from calendar import week
from setuptools import setup
from glob import glob
import os

package_name = 'auto_shepherd_simulation_ros2'
pkg = package_name

setup(
    name=package_name,
    version='0.0.1',
    packages=[package_name],
    data_files=[
        ('share/ament_index/resource_index/packages', [f'resource/{pkg}']),
        (f"share/{pkg}/config", glob(os.path.join('config', '*.rviz'))),
        (f"share/{pkg}/config", glob(os.path.join('config', '*.yaml'))),
        (f'share/{pkg}', ['package.xml']),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='james',
    maintainer_email='primordia@live.com',
    description='TODO: Package description',
    license='TODO: License declaration',
    entry_points={
        'console_scripts': [
            f'sim_data_loader.py = {pkg}.sim_data_loader:main',
            f'boid_training_simulator.py = {pkg}.boid_training_simulator:main',
            f'dog_control.py = {pkg}.dog_control:main',
            f'sheep_simulator.py = {pkg}.sheep_simulator:main',
            f'mapper.py = {pkg}.mapper:main'
        ],
    },
)
